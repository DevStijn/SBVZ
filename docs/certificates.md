# UZI-servercertificaat installeren

Voor `Acceptance` is een UZI-testservercertificaat nodig. Voor `Production`
wordt hetzelfde proces uitgevoerd met een apart productiecertificaat en een
nieuwe private key.

```text
private-key.pem
request.csr
certificate.pem
uzi-server-intermediate.pem
zorg-csp-intermediate.pem
acceptance-root.pem
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

Het UZI-register kan hetzelfde certificaat als DER-bestand (`.cer`) en als
PEM-bestand (`.txt`) meesturen. Controleer eerst het bestandsformaat:

```shell
file issued-certificate.cer issued-certificate.txt
```

Controleer bij twee bestanden dat ze hetzelfde certificaat bevatten. De twee
uitkomsten moeten gelijk zijn:

```shell
openssl x509 \
  -inform DER \
  -in issued-certificate.cer \
  -outform DER | openssl sha256

openssl x509 \
  -in issued-certificate.txt \
  -outform DER | openssl sha256
```

Normaliseer één van de bestanden naar PEM. Gebruik voor een ontvangen
PEM-bestand:

```shell
openssl x509 \
  -in issued-certificate.txt \
  -out certificate.pem
```

Gebruik voor een ontvangen DER-bestand:

```shell
openssl x509 \
  -inform DER \
  -in issued-certificate.cer \
  -out certificate.pem
```

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
  -sha256 \
  -ext subjectAltName,extendedKeyUsage,keyUsage,certificatePolicies
```

Controleer dat de Common Name en DNS Subject Alternative Name overeenkomen met
de FQDN uit de aanvraag. Voor SBV-Z moet het certificaat geschikt zijn voor TLS
client-authenticatie.

### Abonneenummer

Toon de Subject Alternative Name:

```shell
openssl x509 \
  -in certificate.pem \
  -noout \
  -ext subjectAltName
```

Het UZI SubjectID staat in `otherName` met OID `2.5.5.5`. Bij een
acceptatiecertificaat is het abonneenummer de reeks van acht cijfers na `-S-`.
Gebruik dit nummer voor `SBVZ_SUBSCRIBER_NUMBER`. Het pasnummer en UZI-nummer
uit de begeleidende e-mail zijn geen abonneenummer.

### G4-acceptatieketen

Onderstaande keten hoort bij certificaten met issuer
`ACCEPTATIE UZI Server - G4 Priv G-TLS SYS - 2025`. Controleer bij een andere
issuer de Authority Information Access van het certificaat en gebruik de
bijbehorende CA-certificaten van het UZI-register.

```shell
curl -fsSL \
  "http://www.uzi-register-test.nl/cacerts/acceptatie_uzi_server-g4_priv_g-tls_sys-2025.cer" |
  openssl x509 -inform DER -out uzi-server-intermediate.pem

curl -fsSL \
  "http://www.uzi-register-test.nl/cacerts/acceptatie_zorg_csp-g4_intm_priv_g-tls_sys-2024.cer" |
  openssl x509 -inform DER -out zorg-csp-intermediate.pem

curl -fsSL \
  "http://www.uzi-register-test.nl/cacerts/acceptatie_zorg_csp-g4_root_priv_g-tls-2024.cer" |
  openssl x509 -inform DER -out acceptance-root.pem
```

Vergelijk de SHA-256-fingerprints met het actuele
[naamgevingsdocument van de UZI-acceptatieomgeving](https://www.uziregister.nl/documenten/2026/04/23/20260423-naamgevingsdocument-acceptatieomgeving-cibg-zorg-csp-g4-v1.6):

```shell
openssl x509 -in uzi-server-intermediate.pem -noout -fingerprint -sha256
openssl x509 -in zorg-csp-intermediate.pem -noout -fingerprint -sha256
openssl x509 -in acceptance-root.pem -noout -fingerprint -sha256
```

De fingerprints voor deze keten zijn:

```text
ACCEPTATIE UZI Server G4 2025:
B2:F2:B3:37:18:52:D3:B5:75:74:A9:1E:91:63:99:57:47:7A:02:8E:FF:AA:F8:EA:5D:E8:16:07:7D:98:8D:91

ACCEPTATIE Zorg CSP G4 Intermediate 2024:
D3:25:99:70:9F:04:1A:83:59:12:C8:9B:EE:FB:A2:C4:74:EE:B5:76:9F:14:28:0B:52:3C:85:79:C4:C5:4C:75

ACCEPTATIE Zorg CSP G4 Root 2024:
EB:99:A7:26:24:EC:7D:1B:63:3F:8A:84:E6:E3:C9:09:3A:3F:01:F5:F5:EB:0D:6F:6C:7F:37:89:75:A8:60:DC
```

Maak `chain.pem` met alleen de twee tussenliggende CA-certificaten, in volgorde
vanaf de issuer van het servercertificaat. Neem de self-signed root niet op in
de PFX:

```shell
cat \
  uzi-server-intermediate.pem \
  zorg-csp-intermediate.pem \
  > chain.pem
```

Controleer de volledige keten:

```shell
openssl verify \
  -purpose sslclient \
  -show_chain \
  -CAfile acceptance-root.pem \
  -untrusted chain.pem \
  certificate.pem
```

De controle moet eindigen met `certificate.pem: OK`.

### Private key controleren

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
openssl rand -base64 48 > client-certificate-password
chmod 600 client-certificate-password
```

Combineer de private key, het certificaat en de chain:

```shell
openssl pkcs12 \
  -export \
  -name "<fqdn>" \
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
SBVZ_SUBSCRIBER_NUMBER=<acht-cijferig-test-abonneenummer-uit-certificaat>
SBVZ_CLIENT_CERTIFICATE_PATH=/absolute/path/to/client.pfx
SBVZ_CLIENT_CERTIFICATE_PASSWORD_FILE=/absolute/path/to/client-certificate-password
```

Gebruik voor productie dezelfde variabelen met `SBVZ_MODE=Production`, het
abonneenummer uit het productiecertificaat en de bestanden van het afzonderlijk
gegenereerde productiecertificaat. Hergebruik de acceptatie-private key niet.

De FQDN identificeert het systeem in het UZI-certificaat. Voor uitgaande
SBV-Z-aanvragen met mTLS hoeft op die FQDN niet ook een website of inkomende
webservice te draaien.
