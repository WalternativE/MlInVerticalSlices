namespace LoadForecasting.App

module LinearForecast =
  open System
  open LoadForecasting.App.Global
  open LoadForecasting.Common
  open Microsoft.AspNetCore.Http
  open Giraffe
  open FSharp.Control.Tasks.NonAffine
  open Microsoft.Extensions.ML

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
