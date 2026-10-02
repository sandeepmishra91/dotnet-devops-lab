FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
# Generate and commit packages.lock.json on the laptop before using this build.
RUN dotnet restore DevOpsLab.slnx --locked-mode
RUN dotnet build DevOpsLab.slnx -c Release --no-restore

FROM build AS publish
RUN dotnet publish src/PaymentRequest.Api/PaymentRequest.Api.csproj \
    -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=publish /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "PaymentRequest.Api.dll"]
