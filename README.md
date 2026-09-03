# SBVZ

Interne HTTP-service voor het opvragen en verifiëren van BSN's via SBV-Z.

## Vereisten

- .NET SDK 10.0.400
- toegang tot een S3-compatible auditopslag

De SDK-versie wordt vastgezet in `global.json`. Controleer na installatie:

```shell
dotnet --version
```

## Lokale configuratie

Maak een lokale configuratie aan:

```shell
cp .env.example .env
```

Vul minimaal het abonneenummer, de interne API-sleutel, de audit-HMAC-sleutel en
de gegevens van de auditopslag in. Genereer de twee lokale sleutels afzonderlijk:

```shell
openssl rand -base64 32
openssl rand -base64 32
```

Gebruik de eerste waarde voor `SBVZ_API_KEY` en de tweede voor
`SBVZ_AUDIT_PATIENT_REFERENCE_KEY`. `.env` wordt uitsluitend in Development
geladen.

## Development

Start de applicatie met hot reload:

```shell
dotnet watch --project src/Sbvz.Api
```

Het launch-profiel gebruikt `http://localhost:5000` en zet de omgeving op
`Development`. De beschikbare lokale routes zijn:

```text
GET  /health
GET  /openapi/v1.json
GET  /scalar/v1
POST /v1/bsn/lookup
POST /v1/bsn/verify
```

OpenAPI en Scalar worden alleen in Development aangeboden. `Mock` gebruikt geen
UZI-certificaat en maakt geen verbinding met SBV-Z, maar schrijft geaccepteerde
operaties wel naar de geconfigureerde auditopslag.

## Controles

```shell
dotnet format SBVZ.sln --verify-no-changes --no-restore
dotnet build SBVZ.sln --configuration Release
dotnet test SBVZ.sln --configuration Release --no-build
dotnet list SBVZ.sln package --vulnerable --include-transitive
```

Compiler- en analyzerwaarschuwingen breken de build.

## Docker

```shell
docker compose up --build
```

De service is dan bereikbaar op `http://127.0.0.1:8080`. Compose mount de
secretmap uit `.env` read-only op `/run/secrets/sbvz`.

## Omgevingen

`SBVZ_MODE` is verplicht en ondersteunt:

- `Mock`: lokale vaste antwoorden, zonder UZI-certificaat of SBV-Z-verkeer;
- `Acceptance`: echte SBV-Z-acceptatieomgeving met UZI-testcertificaat;
- `Production`: echte SBV-Z-productieomgeving met productiecertificaat.

## Certificaat

Zie [docs/certificates.md](docs/certificates.md) voor het maken van de private
key en CSR en het installeren van een ontvangen UZI-servercertificaat.

## Licentie

Copyright 2026 D3V B.V. All rights reserved. Zie [LICENSE](LICENSE).
