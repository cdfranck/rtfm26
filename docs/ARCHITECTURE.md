# Architecture

## Overview

`rtfm26` is a lightweight ASP.NET Core (`net10.0`) blog app with:
- Public post and page rendering
- Browser-based admin for posts/pages/uploads
- Microsoft account authentication and single-admin authorization
- Markdown rendering for post/page content
- JSON + filesystem persistence on local disk

## Main Components

- `Program.cs`
  - Defines all HTTP routes using minimal APIs
  - Renders HTML for public/admin pages
  - Configures cookie + OpenID Connect authentication (Microsoft Entra ID)
  - Handles upload pipeline and markdown rendering
- `Models/BlogPost.cs`
  - Post shape: `Id`, `Title`, `Slug`, `Content`, `UpdatedUtc`
- `Models/SitePage.cs`
  - Static page shape: `Id`, `Title`, `Slug`, `Content`, `UpdatedUtc`
- `Services/BlogStore.cs`
  - File-backed storage abstraction
  - Reads/writes `App_Data/posts.json`
  - Uses a lock for thread-safe file access
- `Services/PageStore.cs`
  - File-backed page storage abstraction
  - Reads/writes `App_Data/pages.json`
  - Uses a lock for thread-safe file access

## Route Map

- `GET /`
  - Shows all posts sorted by most recently updated
- `GET /post/{slug}`
  - Shows one post by slug
- `GET /page/{slug}`
  - Shows one static page by slug
- `GET /admin`
  - Displays post/page editors, lists, upload tool
- `GET /login`
  - Sign-in page for Microsoft account authentication
- `POST /admin/save`
  - Creates or updates a post from form fields
- `POST /admin/delete/{id}`
  - Deletes a post by id
- `POST /admin/page/save`
  - Creates or updates a page
- `POST /admin/page/delete/{id}`
  - Deletes a page
- `POST /admin/upload`
  - Saves upload into `App_Data/uploads`
- `GET /health`
  - Health/status endpoint
- `GET /error`
  - Generic error response

## Data Flow

1. Admin signs in with Microsoft account and opens `/admin`.
2. Admin edits post/page content (Markdown) and submits.
3. Server validates title/slug/content and normalizes slug.
4. Store service writes JSON (`posts.json` or `pages.json`).
5. Public routes render markdown to HTML.
6. Uploads are stored under `App_Data/uploads` and referenced inline.

## Current Constraints

- Single admin account based on one allowed email address.
- Markdown is trusted admin input (no additional HTML sanitizer).
- File-based storage is not suitable for multi-instance write coordination.
