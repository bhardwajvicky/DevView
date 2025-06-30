# BB (Bitbucket Analytics)

## Overview
BB is a .NET 9 solution designed to fetch, store, and analyze code changes (commits and pull requests) from Bitbucket Cloud across multiple repositories. The project integrates with Bitbucket APIs, stores commit and PR data in a SQL database, and exposes analysis endpoints via a clean, modular API.

## Features
- Integration with Bitbucket Cloud REST APIs for commits and pull requests
- Data storage in a SQL database (schema managed via SQL files, no migrations)
- Modular API endpoints for analysis of code changes
- DTOs and endpoint logic organized for clarity and maintainability

## Tech Stack
- Language: C#
- Framework: .NET 9
- Database: SQL (schema in `SqlSchema/schema.sql`)
- Bitbucket API: [Commits](https://developer.atlassian.com/cloud/bitbucket/rest/api-group-commits/#api-group-commits), [Pull Requests](https://developer.atlassian.com/cloud/bitbucket/rest/api-group-pullrequests/#api-group-pullrequests)

## Project Structure
```
BB.sln
│
├── BBIntegration/                    # Class Library for Bitbucket API integration
│   ├── Commits/                      # Commit-related integration logic
│   │   ├── BitbucketCommitsService.cs
│   │   └── CommitDto.cs
│   ├── PullRequests/                 # PR-related integration logic
│   │   ├── BitbucketPullRequestsService.cs
│   │   └── PullRequestDto.cs
│   ├── Common/                       # Shared helpers, Bitbucket API client, config, etc.
│   │   ├── BitbucketApiClient.cs
│   │   └── BitbucketConfig.cs
│
├── BB.Api/                           # ASP.NET Core Web API project
│   ├── Endpoints/                    # Each API endpoint in its own folder
│   │   ├── Commits/                  # Commits endpoint
│   │   │   ├── CommitsController.cs
│   │   │   ├── CommitRequestDto.cs
│   │   │   └── CommitResponseDto.cs
│   │   ├── PullRequests/             # PullRequests endpoint
│   │   │   ├── PullRequestsController.cs
│   │   │   ├── PullRequestRequestDto.cs
│   │   │   └── PullRequestResponseDto.cs
│   ├── Services/                     # Business/analysis logic
│   │   ├── CommitAnalysisService.cs
│   │   └── PullRequestAnalysisService.cs
│   ├── Models/                       # Entity models for DB
│   │   ├── Commit.cs
│   │   └── PullRequest.cs
│   ├── SqlSchema/                    # Folder for SQL schema files
│   │   └── schema.sql
│   └── appsettings.json              # Configuration (DB, Bitbucket, etc.)
│
└── README.md
```

## Notes
- No test project is included.
- No DB migrations are used; manage schema via SQL files.
- Each API endpoint has its own folder with controller and DTOs.
- Integration logic is grouped by feature in the integration class library. 