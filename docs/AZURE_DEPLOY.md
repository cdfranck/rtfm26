# Azure Deployment

## Important Runtime Note

Your existing Linux App Service is currently configured for Node 24.
This .NET app will not run correctly until you switch the runtime stack to `.NET 10`.

## 1) Switch Existing App Service Runtime

Replace placeholders:
- `<resource-group>`
- `<webapp-name>`

```bash
az webapp config set --resource-group <resource-group> --name <webapp-name> --linux-fx-version "DOTNETCORE|10.0"
```

## 2) Build and Package Locally

From repo root:

```powershell
.\scripts\package-azure.ps1
```

Output zip:
- `artifacts/rtfm26.zip`

## 3) Deploy ZIP Package

```bash
az webapp deploy --resource-group <resource-group> --name <webapp-name> --src-path ./artifacts/rtfm26.zip --type zip
```

## 4) Configure App Settings (Recommended)

```bash
az webapp config appsettings set --resource-group <resource-group> --name <webapp-name> --settings ASPNETCORE_ENVIRONMENT=Production
```

## 5) Configure Microsoft Login (Required for `/admin`)

Create an Entra ID app registration:
- Microsoft Entra ID -> App registrations -> New registration
- Supported account types: single tenant (recommended)
- Redirect URI (Web): `https://<webapp-name>.azurewebsites.net/signin-oidc`

Create a client secret in the app registration and copy the value.

Set app settings:

```bash
az webapp config appsettings set --resource-group <resource-group> --name <webapp-name> --settings \
Authentication__Microsoft__TenantId="<tenant-id>" \
Authentication__Microsoft__ClientId="<client-id>" \
Authentication__Microsoft__ClientSecret="<client-secret>" \
Authentication__Microsoft__CallbackPath="/signin-oidc" \
Admin__AllowedEmail="<your-microsoft-email>"
```

## 6) Verify

- Home: `https://<webapp-name>.azurewebsites.net/`
- Login: `https://<webapp-name>.azurewebsites.net/login`
- Admin: `https://<webapp-name>.azurewebsites.net/admin`
- Example page route: `https://<webapp-name>.azurewebsites.net/page/<slug>`
- Example post route: `https://<webapp-name>.azurewebsites.net/post/<slug>`
- Health: `https://<webapp-name>.azurewebsites.net/health`

## GitHub Actions Deployment

This repo includes:
- `.github/workflows/deploy-azure-webapp.yml`

Set these in GitHub repo settings:
- Repository variable: `AZURE_WEBAPP_NAME`
- Repository secret: `AZURE_WEBAPP_PUBLISH_PROFILE`

How to get publish profile:
- Azure Portal -> App Service -> Get publish profile

Then run workflow:
- GitHub -> Actions -> `Deploy Azure Web App` -> `Run workflow`

## Operational Caveat

Content is stored in local files:
- `App_Data/posts.json`
- `App_Data/pages.json`
- `App_Data/uploads/*`

For production durability and scale-out, migrate persistence to a managed data store.
