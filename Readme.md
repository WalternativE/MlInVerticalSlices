# ML In Vertical Slices

This repository contains the code and presentation for the conference talk "ML In Vertical Slices" I created
for the ML.NET Community Conference 2021.

## Prerequisites

To run the code you will need

- VS Code Insiders
- .NET Interactive Extension
- Ionide Extension
- .NET SDK 5.0.x

## Run the artifacts

Trained models need to be hosted locally to be picked up by the ASP.NET Core app. This can be done using `dotnet-serve`.
Run the following commands in the root directory of this repository.

```bash
dotnet tool restore
```

```bash
dotnet serve -d ./models/ -p 8081
```

To run the ASP.NET Core application run the following command in the root directory of this repository.

```bash
dotnet run -p ./src/LoadForecasting.App/LoadForecasting.App.fsproj
```
