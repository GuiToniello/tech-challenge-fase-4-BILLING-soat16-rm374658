# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia primeiro os csproj para maximizar o cache do restore.
# Se só o código mudar, essa etapa tende a ser reaproveitada.
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.API/TechChallenge.Oficina.Billing.API.csproj", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.API/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.DB/TechChallenge.Oficina.Billing.DB.csproj", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.DB/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Email/TechChallenge.Oficina.Billing.Email.csproj", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Email/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Messaging/TechChallenge.Oficina.Billing.Messaging.csproj", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Messaging/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.NoSql/TechChallenge.Oficina.Billing.NoSql.csproj", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.NoSql/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Pagamentos/TechChallenge.Oficina.Billing.Pagamentos.csproj", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Pagamentos/"]
COPY ["2 - Interface Adapters/TechChallenge.Oficina.Billing.Adapters/TechChallenge.Oficina.Billing.Adapters.csproj", "2 - Interface Adapters/TechChallenge.Oficina.Billing.Adapters/"]
COPY ["2 - Interface Adapters/TechChallenge.Oficina.Billing.Controllers/TechChallenge.Oficina.Billing.Controllers.csproj", "2 - Interface Adapters/TechChallenge.Oficina.Billing.Controllers/"]
COPY ["3 - Application Business Rules/TechChallenge.Oficina.Billing.UseCases/TechChallenge.Oficina.Billing.UseCases.csproj", "3 - Application Business Rules/TechChallenge.Oficina.Billing.UseCases/"]
COPY ["4 - Enterprise Business Rules/TechChallenge.Oficina.Billing.Entities/TechChallenge.Oficina.Billing.Entities.csproj", "4 - Enterprise Business Rules/TechChallenge.Oficina.Billing.Entities/"]

RUN dotnet restore "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.API/TechChallenge.Oficina.Billing.API.csproj"

# Depois do restore, copia apenas os projetos necessários para compilar a solução.
# Isso evita trazer arquivos extras do contexto de build para a imagem.
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.API/", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.API/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.DB/", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.DB/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Email/", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Email/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Messaging/", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Messaging/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.NoSql/", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.NoSql/"]
COPY ["1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Pagamentos/", "1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.Pagamentos/"]
COPY ["2 - Interface Adapters/TechChallenge.Oficina.Billing.Adapters/", "2 - Interface Adapters/TechChallenge.Oficina.Billing.Adapters/"]
COPY ["2 - Interface Adapters/TechChallenge.Oficina.Billing.Controllers/", "2 - Interface Adapters/TechChallenge.Oficina.Billing.Controllers/"]
COPY ["3 - Application Business Rules/TechChallenge.Oficina.Billing.UseCases/", "3 - Application Business Rules/TechChallenge.Oficina.Billing.UseCases/"]
COPY ["4 - Enterprise Business Rules/TechChallenge.Oficina.Billing.Entities/", "4 - Enterprise Business Rules/TechChallenge.Oficina.Billing.Entities/"]
WORKDIR "/src/1 - Frameworks & Drivers/TechChallenge.Oficina.Billing.API"
RUN dotnet publish "TechChallenge.Oficina.Billing.API.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Instala uma ferramenta leve de HTTP para o HEALTHCHECK do container.
RUN apt-get update \
	&& apt-get install -y --no-install-recommends curl \
	&& rm -rf /var/lib/apt/lists/*

# Usa apenas o runtime para manter a imagem final menor e mais segura.
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

# A imagem oficial já expõe um usuário não-root via APP_UID.
USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
	CMD curl --fail --silent --show-error http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "TechChallenge.Oficina.Billing.API.dll"]
