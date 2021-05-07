namespace LoadForecasting.App

module Global =
  open System

  [<Literal>]
  let ModelBaseUri = "http://localhost:8081"

  [<Literal>]
  let LinearModelName = "LinearForecast"

  [<Literal>]
  let SSAModelName = "SSAModel"

  // first tick after the training set
  [<Literal>]
  let tickOffset = 35064

  // first timestamp after the training set
  let dateTimeOffset = DateTime.Parse("2018-12-31T23:00:00")

  type ForecastRequest = { From: DateTime; Horizon: int }

  let hoursSinceOffset (from: DateTime) =
    (DateTimeOffset(from)
     - DateTimeOffset(dateTimeOffset))
      .TotalHours
