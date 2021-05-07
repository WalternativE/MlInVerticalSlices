namespace LoadForecasting.App

module SSAForecast =
  open System
  open FSharp.Control.Tasks.NonAffine
  open Microsoft.AspNetCore.Http
  open Microsoft.Extensions.ML
  open Giraffe
  open Microsoft.ML
  open Microsoft.ML.Transforms.TimeSeries
  open Microsoft.Extensions.DependencyInjection
  open LoadForecasting.Common
  open LoadForecasting.App.Global

  let ssaPredictionhandler : HttpHandler =
    fun (next: HttpFunc) (context: HttpContext) ->
      task {
        let! forecastRequest = context.BindJsonAsync<ForecastRequest>()

        let mlContext = context.GetService<MLContext>()

        // Why isn't there a Time Series Prediction Engine Pool?
        // See: https://github.com/dotnet/machinelearning-samples/issues/739#issuecomment-552004422
        // ========================================================================================================
        // BEWARE: this is an extremely dirty hack - never do this ever
        // I'd rather go and open a ticket with dotnet/machinelearning to add a timesereis forecasting engine pool
        let poolBuilder =
          context.GetService<PredictionEnginePoolBuilder<AlternativeForecastInput, AlternativeLoadForecast>>()

        let poolBuilderType = poolBuilder.GetType()
        let poolBuilderAssembly = Reflection.Assembly.GetAssembly(poolBuilderType)

        let modelLoaderType =
          poolBuilderAssembly.GetType("Microsoft.Extensions.ML.UriModelLoader")

        let modelLoader : ModelLoader =
          poolBuilder
            .Services
            .BuildServiceProvider()
            .GetService(modelLoaderType)
          :?> ModelLoader

        let startMethod =
          modelLoaderType.GetMethod(
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

        return! Successful.OK forecast next context
      }
