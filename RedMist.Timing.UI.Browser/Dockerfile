FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

#RUN dotnet workload restore
RUN dotnet nuget locals all --clear
RUN dotnet workload update
RUN dotnet workload install wasm-tools
RUN apt-get update
RUN apt-get install -y python3

# Copy the project files and restore dependencies
WORKDIR /src
COPY ["RedMist.Timing.UI.Browser/RedMist.Timing.UI.Browser.csproj", "RedMist.Timing.UI.Browser/"]
COPY ["RedMist.Timing.UI/RedMist.Timing.UI.csproj", "RedMist.Timing.UI/"]
COPY ["redmist-timing-common/RedMist.TimingCommon/RedMist.TimingCommon.csproj", "redmist-timing-common/RedMist.TimingCommon/"]
COPY ["bigmission-shared/BigMission.Shared/BigMission.Shared.csproj", "bigmission-shared/BigMission.Shared/"]
#COPY ["nuget.config", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
#RUN dotnet restore "./RedMist.Timing.UI.Browser/RedMist.Timing.UI.Browser.csproj" 
# --verbosity:diag

COPY . .

WORKDIR "/src/RedMist.Timing.UI.Browser"


# Build and publish the application
RUN dotnet publish "./RedMist.Timing.UI.Browser.csproj" -p:PublishTrimmed=false

# Use Nginx to serve the WebAssembly files
FROM nginx:alpine

## Copy the published output to the Nginx web root
COPY --from=build /src/RedMist.Timing.UI.Browser/bin/Release/net9.0-browser/publish/wwwroot /usr/share/nginx/html/

# Configure Nginx to serve WASM correctly
COPY --from=build /src/RedMist.Timing.UI.Browser/site.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/RedMist.Timing.UI.Browser/nginx.conf /etc/nginx/nginx.conf

# Expose port 80
EXPOSE 80

## Start Nginx
CMD ["nginx", "-g", "daemon off;"]
