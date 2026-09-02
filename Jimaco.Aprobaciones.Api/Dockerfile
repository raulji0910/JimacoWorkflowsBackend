FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Jimaco.Aprobaciones.slnx .
COPY Jimaco.Aprobaciones.Modelo/Jimaco.Aprobaciones.Modelo.csproj Jimaco.Aprobaciones.Modelo/
COPY Jimaco.Aprobaciones.Negocio/Jimaco.Aprobaciones.Negocio.csproj Jimaco.Aprobaciones.Negocio/
COPY Jimaco.Aprobaciones.Api/Jimaco.Aprobaciones.Api.csproj Jimaco.Aprobaciones.Api/
COPY Jimaco.Aprobaciones.TestUnitarios/Jimaco.Aprobaciones.TestUnitarios.csproj Jimaco.Aprobaciones.TestUnitarios/
RUN dotnet restore Jimaco.Aprobaciones.Api/Jimaco.Aprobaciones.Api.csproj

COPY Jimaco.Aprobaciones.Modelo/ Jimaco.Aprobaciones.Modelo/
COPY Jimaco.Aprobaciones.Negocio/ Jimaco.Aprobaciones.Negocio/
COPY Jimaco.Aprobaciones.Api/ Jimaco.Aprobaciones.Api/

RUN dotnet publish Jimaco.Aprobaciones.Api/Jimaco.Aprobaciones.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# El contenedor corre como usuario no-root ($APP_UID) por seguridad. El volumen de adjuntos
# (named volume, montado en /adjuntos) se inicializa la primera vez copiando el contenido y
# los permisos de esta carpeta de la imagen — por eso hay que crearla y darle dueño ACÁ, antes
# de montar el volumen, o el proceso no-root no puede escribir ahí (queda root:root 755).
RUN mkdir -p /adjuntos && chown $APP_UID /adjuntos

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Jimaco.Aprobaciones.Api.dll"]
