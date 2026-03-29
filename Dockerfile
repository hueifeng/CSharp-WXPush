FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["csharp-wxpush.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5566
ENTRYPOINT ["dotnet", "csharp-wxpush.dll"]
CMD ["-port", "5566", "-appid", "", "-secret", "", "-userid", "", "-template_id", "", "-base_url", "", "-title", "", "-content", "", "-tz", ""]
