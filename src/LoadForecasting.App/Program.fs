module LoadForecasting.App.App

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.ML
open Microsoft.ML
open Giraffe
open LoadForecasting.Common
open LoadForecasting.App.Global
open LoadForecasting.App.LinearForecast
open LoadForecasting.App.SSAForecast

// ---------------------------------
// App routes
// ---------------------------------

let webApp =
  choose [ GET
           >=> choose [ route "/" >=> text "Yeah, I'm running." ]
           POST
           >=> choose [ route "/linear-forecast" >=> linearPredictionHandler
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

  // ===================== PART of a bad bad hack =========================================================
  let poolBuilder =
    PredictionEnginePoolBuilder<AlternativeForecastInput, AlternativeLoadForecast>(services)
      .FromUri(modelName = SSAModelName, uri = $"{ModelBaseUri}/forecast_model.zip", period = TimeSpan.FromMinutes(1.))

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
