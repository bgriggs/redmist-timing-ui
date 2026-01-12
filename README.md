# Red Mist Timing Frontend App
[![Build](https://github.com/bgriggs/redmist-timing-ui/actions/workflows/build.yml/badge.svg)](https://github.com/bgriggs/redmist-timing-scoring-backend/actions/workflows/build.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/download)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Red Mist provides race timing and scoring services for motorsport events. This repository contains the frontend application for the Red Mist Timing system, built with Avalonia. It is designed to be run on iOS, Android, Browsers, Windows, Linux, macOS, and other platforms.

# Building the Application
Reference the Avalonia documentation for editor setup:
https://docs.avaloniaui.net/docs/get-started/set-up-an-editor

You can build the application using the included solution file `RedMist.Timing.UI.sln` in Visual Studio or using the .NET CLI. To build from the command line, navigate to the project directory and run:
```bash
dotnet build RedMist.Timing.UI.sln
```

# Running the Application
For development, the most direct way to run the application is with the Windows Desktop variant of Avalonia. You can set it as the Startup Project, or run the exe from the debug folder.

## Client Key
To run the application, you will need a client key. This key is used to authenticate your instance of the application with the Red Mist Timing backend. Contact support@redmist.racing to request an API key. Alternatively, you can run the backend and a Keycloak authentication server.

# Development
The main project is `RedMist.Timing.UI`, which contains the Avalonia application code for the majority of the UI. The other projects are there for builds to specific environments, e.g. iOS. There are also shared libraries for data models.

## Event Only Mode
When hosting the application in the web, the event selection is delegated to the website. Therefore, the application directly routes to the event in the mode. It uses the page URL with event parameter to accomplish this. For development, you can pass the event ID as a command line argument to the application. For example, set the args in the Desktop launchSettings.json:
```bash
"commandLineArgs": "29"
```
Where 29 is the event ID.


