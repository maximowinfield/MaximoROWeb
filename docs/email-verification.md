# Email Verification Setup

New MaximoRO accounts are created in rAthena state `11` ("Email not
confirmed"). A valid verification link changes the account to state `0`,
which allows normal game login.

Registration remains unavailable until every required email setting is valid.
Keep SMTP credentials out of `appsettings.json`; use .NET user secrets locally
and environment variables or a secret manager in production. The MaximoRO
single-machine launcher imports the required values from the current Windows
user's .NET user-secret store into the Production process at startup.

## Local configuration

Run these commands from the project directory, replacing the example values:

```powershell
dotnet user-secrets set "EmailVerification:PublicBaseUrl" "https://your-domain.example"
dotnet user-secrets set "EmailVerification:Smtp:Enabled" "true"
dotnet user-secrets set "EmailVerification:Smtp:Host" "smtp.provider.example"
dotnet user-secrets set "EmailVerification:Smtp:Port" "587"
dotnet user-secrets set "EmailVerification:Smtp:EnableSsl" "true"
dotnet user-secrets set "EmailVerification:Smtp:Username" "smtp-username"
dotnet user-secrets set "EmailVerification:Smtp:Password" "smtp-password"
dotnet user-secrets set "EmailVerification:Smtp:FromAddress" "no-reply@your-domain.example"
dotnet user-secrets set "EmailVerification:Smtp:FromName" "MaximoRO"
```

The `PublicBaseUrl` must be the public HTTPS origin that players use. It is
trusted when verification links are generated, so it must not be derived from
incoming request headers.

SMTP providers that authenticate by IP may allow both `Username` and
`Password` to remain blank. Otherwise, configure both.

## Database

The application creates the verification tables on startup. The equivalent
manual migration is in `Database/001_email_verification.sql`.

The new tables use InnoDB and intentionally do not add a foreign key to
rAthena's `login` table, which may use MyISAM.

## Production checklist

- Run the MaximoRO launcher under the same Windows account that owns the .NET
  user secrets.
- Confirm the deployment health check reports that the registration form is
  available.
- For any future multi-machine hosting, move the settings to that platform's
  environment-variable or secret-management system.
- Configure SPF, DKIM, and DMARC for the sender domain through the mail
  provider.
- Verify that the sender address is authorized by the provider.
- Register a test account and confirm the game client refuses it before email
  verification.
- Open the verification link and confirm the account can then sign in.
- Test the resend page and check spam-folder placement.
