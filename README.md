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

Vul minimaal het abonneenummer, de API-client-id, de audit-HMAC-sleutel en de
gegevens van de auditopslag in. Maak de interne API-sleutel en bijbehorende hash
aan in de lokale secretmap:

```shell
dotnet run --project tools/Sbvz.Credentials -- api \
  --output /absolute/path/to/sbvz/certificates/test
```

Genereer daarnaast een afzonderlijke sleutel voor patiëntreferenties:

```shell
openssl rand -base64 32
```

De aanroepende applicatie gebruikt `api-key`. De SBV-Z-service gebruikt alleen
`api-key-sha256`; in Development mag de service de hash ook afleiden uit
`SBVZ_API_KEY` of `SBVZ_API_KEY_FILE`. Gebruik de tweede waarde voor
`SBVZ_AUDIT_PATIENT_REFERENCE_KEY`. `.env` wordt uitsluitend in Development geladen.

## Development

Start de applicatie met hot reload:

```shell
dotnet watch --project src/Sbvz.Api
```

Het launch-profiel gebruikt `http://localhost:5080` en zet de omgeving op
`Development`. De beschikbare lokale routes zijn:

```text
GET  /health
GET  /openapi/v1.json
GET  /scalar/v1
POST /v1/bsn/lookup
POST /v1/bsn/verify
```

OpenAPI en Scalar worden alleen in Development aangeboden. Ook lokaal maakt de
applicatie verbinding met de gekozen SBV-Z-omgeving en is een passend
UZI-servercertificaat vereist.

## Controles

```shell
dotnet format SBVZ.sln --verify-no-changes --severity info --no-restore
dotnet build SBVZ.sln --configuration Release
dotnet test SBVZ.sln --configuration Release --no-build
dotnet list SBVZ.sln package --vulnerable --include-transitive
```

Compiler- en analyzerwaarschuwingen breken de build.

De gewone tests gebruiken een geïsoleerde testimplementatie en maken geen
verbinding met SBV-Z. De expliciete end-to-end-test controleert beide zoekpaden
met een fictieve persoon uit de openbare RvIG-testdataset en doorloopt daarnaast
alle officiële SBV-Z-scenario's voor zowel opvragen als verifiëren, inclusief
goede, afwijkende en foutresultaten. De test leest de lokale `.env`, schrijft
auditregels en werkt uitsluitend wanneer `SBVZ_MODE=Acceptance`:

```shell
dotnet test tests/Sbvz.Api.EndToEndTests/Sbvz.Api.EndToEndTests.csproj \
  --configuration Release \
  --explicit only
```

## Docker

```shell
docker compose up --build
```

De service is dan bereikbaar op `http://127.0.0.1:8080`. Compose mount de
secretmap uit `.env` read-only op `/run/secrets/sbvz`.

## Omgevingen

`SBVZ_MODE` is verplicht en ondersteunt:

- `Acceptance`: echte SBV-Z-acceptatieomgeving met UZI-testcertificaat;
- `Production`: echte SBV-Z-productieomgeving met productiecertificaat.

## Certificaat

Zie [docs/certificates.md](docs/certificates.md) voor het maken van de private
key en CSR en het installeren van een ontvangen UZI-servercertificaat.

## Licentie

Copyright 2026 D3V B.V. All rights reserved. Zie [LICENSE](LICENSE).
