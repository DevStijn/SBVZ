# UZI-servercertificaat installeren

Voor `Acceptance` is een UZI-testservercertificaat nodig. Voor `Production`
wordt hetzelfde proces uitgevoerd met een apart productiecertificaat en een
nieuwe private key. `Mock` gebruikt geen certificaat.

```text
private-key.pem
request.csr
certificate.pem
chain.pem
client.pfx
client-certificate-password
```

## 1. Private key en CSR maken

Ga naar de gekozen certificaatmap en stel eerst veilige bestandsrechten in:

```shell
umask 077
```

Maak een versleutelde RSA-private key van 4096 bits:

```shell
openssl genpkey \
  -algorithm RSA \
  -aes-256-cbc \
  -pkeyopt rsa_keygen_bits:4096 \
  -out private-key.pem
```

Maak de PKCS#10-aanvraag. Vervang `<fqdn>` door de domeinnaam uit de
UZI-aanvraag:

```shell
openssl req \
  -new \
  -sha256 \
  -key private-key.pem \
  -out request.csr \
  -subj "/CN=<fqdn>"
```

Controleer de aanvraag:

```shell
openssl req -in request.csr -noout -verify -subject
openssl req -in request.csr -noout -text
```

Upload alleen `request.csr` naar het UZI-register. Deel `private-key.pem` niet.

## 2. Ontvangen certificaat voorbereiden

Sla het uitgegeven certificaat op als `certificate.pem`. Is het ontvangen
bestand DER-gecodeerd, converteer het dan als volgt:

```shell
openssl x509 \
  -inform DER \
  -in issued-certificate.cer \
  -out certificate.pem
```

Sla de door UZI geleverde intermediate certificaten in de juiste volgorde op in
`chain.pem`.

Controleer het ontvangen certificaat:

```shell
openssl x509 \
  -in certificate.pem \
  -noout \
  -subject \
  -issuer \
  -serial \
  -dates \
  -fingerprint \
  -sha256
```

Controleer dat certificaat en private key bij elkaar horen. De twee uitkomsten
moeten gelijk zijn:

```shell
openssl pkey \
  -in private-key.pem \
  -pubout \
  -outform DER | openssl sha256

openssl x509 \
  -in certificate.pem \
  -pubkey \
  -noout | openssl pkey -pubin -outform DER | openssl sha256
```

## 3. PFX maken

Maak een wachtwoordbestand:

```shell
openssl rand -base64 32 > client-certificate-password
chmod 600 client-certificate-password
```

Combineer de private key, het certificaat en de chain:

```shell
openssl pkcs12 \
  -export \
  -out client.pfx \
  -inkey private-key.pem \
  -in certificate.pem \
  -certfile chain.pem \
  -passout file:client-certificate-password

chmod 600 client.pfx
```

Controleer de PFX:

```shell
openssl pkcs12 \
  -in client.pfx \
  -passin file:client-certificate-password \
  -info \
  -noout
```

## 4. Applicatie configureren

Gebruik voor acceptatie:

```dotenv
SBVZ_MODE=Acceptance
SBVZ_CLIENT_CERTIFICATE_PATH=/absolute/path/to/client.pfx
SBVZ_CLIENT_CERTIFICATE_PASSWORD_FILE=/absolute/path/to/client-certificate-password
```

Gebruik voor productie dezelfde variabelen met `SBVZ_MODE=Production` en de
bestanden van het productiecertificaat.
