module LoadForecasting.App.App

open System
open System.IO
open FSharp.Control.Tasks.NonAffine
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.ML
open Microsoft.ML
open Microsoft.ML.Transforms.TimeSeries
open Giraffe
open LoadForecasting.Common

[<Literal>]
let ModelBaseUri = "http://localhost:8081"

[<Literal>]
let LinearModelName = "LinearForecast"

[<Literal>]
let SSAModelName = "SSAModel"

// first tick after the training set
[<Literal>]
let tickOffset = 35064

let dateTimeOffset = DateTime.Parse("2018-12-31T23:00:00")

type ForecastRequest = { From: DateTime; Horizon: int }

// ---------------------------------
// App routes
// ---------------------------------

let hoursSinceOffset (from: DateTime) =
  (DateTimeOffset(from)
   - DateTimeOffset(dateTimeOffset))
    .TotalHours

let createInputsFromRequest (request: ForecastRequest) =
  let hoursSinceOffset = hoursSinceOffset request.From

  let tickStart = tickOffset + (int hoursSinceOffset)

  let rec createInputs (currentTick: int) (currentDate: DateTime) (toCreate: int) =
    match toCreate with
    | 0 -> []
    | _ ->
        let input =
          { Ticks = float32 currentTick
            Target = float32 0.
            DayOfWeek = string currentDate.DayOfWeek
            Month = string currentDate.Month
            PeakOffPeak =
              if currentDate.Hour < 8 || currentDate.Hour > 19 then
                "OffPeak"
              else
                "Peak" }

        input
        :: (createInputs (currentTick + 1) (currentDate.AddHours(1.)) (toCreate - 1))

  createInputs tickStart request.From request.Horizon

let linearPredictionHandler : HttpHandler =
  fun (next: HttpFunc) (context: HttpContext) ->
    task {
      let! forecastRequest = context.BindJsonAsync<ForecastRequest>()

      let predictionEnginePool =
        context.GetService<PredictionEnginePool<ForecastInput, ForecastResult>>()

      let predictions =
        createInputsFromRequest forecastRequest
        |> List.map (fun inp -> predictionEnginePool.Predict(modelName = LinearModelName, example = inp))
        |> List.toArray

      return! Successful.OK predictions next context
    }

let ssaPredictionhandler : HttpHandler =
  fun (next: HttpFunc) (context: HttpContext) ->
    task {
      let! forecastRequest = context.BindJsonAsync<ForecastRequest>()

      let mlContext = context.GetService<MLContext>()

      // ========================================================================================================
      // BEWARE: this is an extremely dirty hack - never do this ever
      // I'd rather go and open a ticket with dotnet/machinelearning to add a timesereis forecasting engine pool
      let poolBuilder =
        context.GetService<PredictionEnginePoolBuilder<AlternativeForecastInput, AlternativeLoadForecast>>()

      let tp = poolBuilder.GetType()
      let assmbl = Reflection.Assembly.GetAssembly(tp)

      let tgtp =
        assmbl.GetType("Microsoft.Extensions.ML.UriModelLoader")

      let modelLoader : ModelLoader =
        poolBuilder
          .Services
          .BuildServiceProvider()
          .GetService(tgtp)
        :?> ModelLoader

      let startMethod =
        tgtp.GetMethod(
          "Start",
          Reflection.BindingFlags.NonPublic
          ||| Reflection.BindingFlags.Public
          ||| Reflection.BindingFlags.Instance
        )

      startMethod.Invoke(
        modelLoader,
        [| Uri($"{ModelBaseUri}/forecast_model.zip")
           TimeSpan.FromMinutes(1.) |]
      )
      |> ignore

      let model = modelLoader.GetModel()

      use tsPredEngine =
        model.CreateTimeSeriesEngine<AlternativeForecastInput, AlternativeLoadForecast>(mlContext)
      // END - bad bad bad hack
      // ============================================================================================

      let hoursSinceOffset =
        hoursSinceOffset forecastRequest.From |> int

      let completeHorizon =
        forecastRequest.Horizon + hoursSinceOffset

      let forecast =
        tsPredEngine.Predict(horizon = completeHorizon)
        |> fun fc ->
             { fc with
                 Forecast = fc.Forecast |> Array.skip hoursSinceOffset
                 LowerBound = fc.LowerBound |> Array.skip hoursSinceOffset
                 UpperBound = fc.UpperBound |> Array.skip hoursSinceOffset }

      //   predictionEnginePool.ReturnPredictionEngine engine

      return! Successful.OK forecast next context
    }

let webApp =
  choose [ GET
           >=> choose [ route "/" >=> text "Yeah, I'm running." ]
           POST
           >=> choose [ route "/linear-forecast"
                        >=> linearPredictionHandler
                        route "/ssa-forecast" >=> ssaPredictionhandler ]
           setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex: Exception) (logger: ILogger) =
  logger.LogError(ex, "An unhandled exception has occurred while executing the request.")

  clearResponse
  >=> setStatusCode 500
  >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder: CorsPolicyBuilder) =
  builder
    .WithOrigins("http://localhost:5000", "https://localhost:5001")
    .AllowAnyMethod()
    .AllowAnyHeader()
  |> ignore

let configureApp (app: IApplicationBuilder) =
  let env =
    app.ApplicationServices.GetService<IWebHostEnvironment>()

  (match env.IsDevelopment() with
   | true -> app.UseDeveloperExceptionPage()
   | false ->
       app
         .UseGiraffeErrorHandler(errorHandler)
         .UseHttpsRedirection())
    .UseCors(configureCors)
    .UseStaticFiles()
    .UseGiraffe(webApp)

let configureServices (services: IServiceCollection) : unit =
  services.AddCors() |> ignore
  services.AddGiraffe() |> ignore

  services.AddSingleton<MLContext>(fun _ -> MLContext(seed = 10))
  |> ignore

  services
    .AddPredictionEnginePool<ForecastInput, ForecastResult>()
    .FromUri(modelName = LinearModelName, uri = $"{ModelBaseUri}/linear_model.zip", period = TimeSpan.FromMinutes(1.))
  |> ignore

  let poolBuilder =
    PredictionEnginePoolBuilder<AlternativeForecastInput, AlternativeLoadForecast>(services)
      .FromUri(modelName = SSAModelName, uri = $"{ModelBaseUri}/forecast_model.zip", period = TimeSpan.FromMinutes(1.))

  // ===================== PART of a bad bad hack =========================================================
  services.AddSingleton<PredictionEnginePoolBuilder<AlternativeForecastInput, AlternativeLoadForecast>>
    (fun _ -> poolBuilder)
  |> ignore
  // ======================================================================================================

let configureLogging (builder: ILoggingBuilder) =
  builder.AddConsole().AddDebug() |> ignore

[<EntryPoint>]
let main args =
  let contentRoot = Directory.GetCurrentDirectory()
  let webRoot = Path.Combine(contentRoot, "WebRoot")

  Host
    .CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(fun webHostBuilder ->
      webHostBuilder
        .UseContentRoot(contentRoot)
        .UseWebRoot(webRoot)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
      |> ignore)
    .Build()
    .Run()

  0
