# rtfm26 blog

Simple ASP.NET Core blog with web-based editing.

## Run locally

- .NET SDK 10

```bash
dotnet restore
dotnet run
```

## Routes

- `/` list blog posts
- `/post/{slug}` view a post
- `/admin` create/delete posts in the browser
- `/health` health check endpoint

## Content storage

Posts are saved to `App_Data/posts.json`.

## Deploy to Azure App Service

```bash
az group create --name rtfm26-rg --location eastus
az appservice plan create --name rtfm26-plan --resource-group rtfm26-rg --sku B1 --is-linux
az webapp create --name <unique-app-name> --resource-group rtfm26-rg --plan rtfm26-plan --runtime "DOTNETCORE|10.0"
dotnet publish -c Release -o ./publish
Compress-Archive -Path ./publish/* -DestinationPath ./publish.zip -Force
az webapp deploy --resource-group rtfm26-rg --name <unique-app-name> --src-path ./publish.zip --type zip
```
