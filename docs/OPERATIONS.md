# Operations Guide

## Local Development

```bash
dotnet restore
dotnet run
```

App endpoints:
- `http://localhost:<port>/`
- `http://localhost:<port>/post/{slug}`
- `http://localhost:<port>/page/{slug}`
- `http://localhost:<port>/admin`
- `http://localhost:<port>/login`
- `http://localhost:<port>/contact`
- `http://localhost:<port>/health`

## Content Management

- Sign in at `/login` with Microsoft account.
- In `/admin`, manage posts and pages:
  - create
  - edit
  - delete
- Use Markdown toolbar buttons in editors.
- Drag-and-drop upload files/images in `/admin`.
- Use insert buttons to place upload markup directly in content.

Storage:
- Posts: `App_Data/posts.json`
- Pages: `App_Data/pages.json`
- Uploads: `App_Data/uploads/*`

## Backup and Restore

Backup:
- Copy `App_Data/posts.json`, `App_Data/pages.json`, and `App_Data/uploads/` to a safe location.

Restore:
- Replace `App_Data/posts.json`, `App_Data/pages.json`, and `App_Data/uploads/` from backup.
- Restart app if needed.

## Azure App Service Notes

Current storage is local filesystem. On Azure App Service:
- Data persists on the app's mounted storage for a single app instance.
- Multi-instance scale-out can cause inconsistent local-file writes.
- Swaps/redeployments can still risk content loss if storage handling changes.

Recommended production upgrade:
- Move blog data to a managed data store (Azure SQL, Cosmos DB, or Blob + index).

## Security Notes

- `/admin` requires Microsoft OpenID Connect sign-in.
- Only `Admin:AllowedEmail` can edit content.
- Use HTTPS only in production.

## Basic Troubleshooting

- Build issues:
  - Run `dotnet clean` then `dotnet build`.
- Missing content:
  - Check `App_Data/posts.json` and `App_Data/pages.json` exist and are valid JSON.
- Cannot save post:
  - Verify app process can write to `App_Data/`.
- Upload issues:
  - Verify `App_Data/uploads` exists and is writable.
  - Confirm file extension is on allowed list.
