# Gmail Verification Email Setup

The MaximoRO website is configured to send verification messages through
Gmail:

- SMTP server: `smtp.gmail.com`
- Port: `587`
- TLS/STARTTLS: enabled
- Sender: the Gmail address stored in .NET user secrets
- Public verification origin:
  `https://desktop-68ka5hg.tail9fc6cc.ts.net:8443`

The normal Google Account password must never be used by the website.

## Create an app password

1. Turn on **2-Step Verification** for the sender's Google Account.
2. Open the Google **App passwords** page.
3. Create an app password named `MaximoRO Website`.
4. Copy the generated 16-character app password.
5. Store it from the project directory:

```powershell
dotnet user-secrets set "EmailVerification:Smtp:Password" "YOUR_16_CHARACTER_APP_PASSWORD"
```

Do not place the app password in `appsettings.json`, commit it to Git, or share
it in chat.

After saving the app password, run the deployment script. The launcher loads
the required values into the Production process without copying them into the
published application. Confirm the deployment health check enables registration,
then test with an email address different from the sender.
