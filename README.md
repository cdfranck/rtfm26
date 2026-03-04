# rtfm26 blog

Custom ASP.NET Core (`.NET 10`) blog/CMS-lite with:
- Microsoft account login for admin
- Post and page management in `/admin`
- Markdown content support
- Drag-and-drop uploads with inline insert tools
- Header menu with page/post navigation

## Documentation

- [Getting Started](./README.md)
- [Architecture](./docs/ARCHITECTURE.md)
- [Operations Guide](./docs/OPERATIONS.md)
- [Azure Deployment](./docs/AZURE_DEPLOY.md)

## Run locally

- .NET SDK 10

```bash
dotnet restore
dotnet run
```

## Routes

- `/` landing page + post list + navigation menu
- `/post/{slug}` view a post
- `/page/{slug}` view a page
- `/admin` manage posts/pages and uploads (Microsoft login required)
- `/login` Microsoft sign-in page
- `/contact` contact page
- `/health` health check endpoint

## Content storage

Stored on local disk:
- Posts: `App_Data/posts.json`
- Pages: `App_Data/pages.json`
- Uploads: `App_Data/uploads/*`

Important:
- `/admin` requires Microsoft sign-in and only `Admin:AllowedEmail` has edit rights.
- Local file storage is fine for single-instance use but not ideal for scaled Azure setups.

## Deploy to Azure App Service

```bash
az group create --name rtfm26-rg --location eastus
az appservice plan create --name rtfm26-plan --resource-group rtfm26-rg --sku B1 --is-linux
az webapp create --name <unique-app-name> --resource-group rtfm26-rg --plan rtfm26-plan --runtime "DOTNETCORE|10.0"
dotnet publish -c Release -o ./publish
Compress-Archive -Path ./publish/* -DestinationPath ./publish.zip -Force
az webapp deploy --resource-group rtfm26-rg --name <unique-app-name> --src-path ./publish.zip --type zip
```
