namespace LoadForecasting.Common

open System
open Microsoft.ML.Data

[<CLIMutable>]
type ForecastInput =
  { Ticks: float32
    [<ColumnName("Label")>]
    Target: float32
    DayOfWeek: string
    Month: string
    PeakOffPeak: string }

[<CLIMutable>]
type ForecastResult =
  { [<ColumnName("Score")>]
    LoadForecast: float32 }

[<CLIMutable>]
type AlternativeForecastInput = { Load: float32; TimeStamp: DateTime }

[<CLIMutable>]
type AlternativeLoadForecast =
  { Forecast: float32 array
    LowerBound: float32 array
    UpperBound: float32 array }
