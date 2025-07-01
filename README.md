# Bitbucket Analytics Dashboard

A comprehensive .NET 9 solution for analyzing Bitbucket repositories with real-time analytics, beautiful visualizations, and GitHub-style insights.

## 🌟 Features

### 📊 Analytics Dashboard
- **Interactive Charts**: Multi-dataset area charts showing code lines added/removed over time
- **Individual Contributors**: GitHub-style contributor activity charts with detailed statistics
- **Date Range Filtering**: Flexible date range selection with quick presets (30 days, 3 months, etc.)
- **Real-time Data**: Live updates when changing repositories or date ranges
- **Responsive Design**: Modern Bootstrap-based UI that works on all devices

### 🔧 Data Management
- **Bitbucket Integration**: Automated syncing of repositories, commits, and pull requests
- **Smart Data Storage**: Optimized SQL schema with proper indexing
- **API-First Architecture**: RESTful endpoints for all operations
- **Advanced Analytics**: Code line analysis, contributor statistics, and activity patterns

### 🚀 Modern Tech Stack
- **Backend**: .NET 9 ASP.NET Core Web API
- **Frontend**: Blazor Server with Radzen UI components
- **Database**: SQL Server with optimized schema
- **Charts**: Chart.js with custom configurations
- **APIs**: Bitbucket Cloud REST API integration

## 🏗️ Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   BB.Web        │    │     BB.Api       │    │  BBIntegration  │
│  (Blazor UI)    │◄──►│  (REST API)      │◄──►│ (Bitbucket API) │
│  Port: 5084     │    │  Port: 5000      │    │   Integration   │
└─────────────────┘    └──────────────────┘    └─────────────────┘
          │                       │                       │
          │                       │                       │
          ▼                       ▼                       ▼
    ┌─────────────────────────────────────────────────────────┐
    │                  SQL Server Database                    │
    │         (Users, Repositories, Commits, PRs)            │
    └─────────────────────────────────────────────────────────┘
```

### Project Structure
```
BB.sln
├── BB.Api/                          # 🔧 ASP.NET Core Web API
│   ├── Endpoints/
│   │   ├── Analytics/               # 📊 Analytics controllers & DTOs
│   │   ├── Commits/                 # 📝 Commit data endpoints
│   │   ├── PullRequests/            # 🔀 Pull request endpoints
│   │   └── Sync/                    # 🔄 Data synchronization
│   ├── Services/                    # 🛠️ Business logic services
│   ├── Models/                      # 📋 Database entity models
│   ├── SqlSchema/                   # 🗄️ Database schema files
│   └── appsettings.json            # ⚙️ API configuration
│
├── BB.Web/                          # 🌐 Blazor Server Web App
│   ├── Components/
│   │   ├── Pages/
│   │   │   ├── Dashboard.razor      # 📊 Main analytics dashboard
│   │   │   └── ApiTest.razor        # 🧪 API connection testing
│   │   └── Layout/                  # 🎨 UI layout components
│   ├── DTOs/                        # 📦 Data transfer objects
│   └── appsettings.json            # ⚙️ Web app configuration
│
├── BBIntegration/                   # 🔌 Bitbucket API Integration
│   ├── Commits/                     # 📝 Commit data fetching
│   ├── PullRequests/               # 🔀 PR data fetching
│   ├── Repositories/               # 📁 Repository management
│   ├── Users/                      # 👥 User data management
│   ├── Common/                     # 🛠️ Shared API client & config
│   └── Utils/                      # 🔧 Utility services
│
└── start-dev.sh                    # 🚀 Development startup script
```

## 🚀 Quick Start

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (or SQL Server Express)
- [Git](https://git-scm.com/)

### 1. Clone & Setup
```bash
git clone <repository-url>
cd easy-api-dev
```

### 2. Configuration Setup
```bash
# Run the configuration setup script
./setup-config.sh
```

This will:
- Copy configuration templates to actual config files
- Provide instructions for filling in sensitive values
- Make startup scripts executable

### 3. Database Setup
1. **Create Database**: Create a new database named `bb` in SQL Server
   ```sql
   CREATE DATABASE bb;
   ```
2. **Run Schema**: Execute the complete SQL schema from `BB.Api/SqlSchema/schema.sql` to create all tables:
   - Users table (with avatar support)
   - Repositories table 
   - Commits table (with code line tracking)
   - PullRequests table
   - All necessary indexes and foreign key relationships
3. **Configure Connection**: Update the connection string in `BB.Api/appsettings.json` (created from template)

### 4. Start Development Environment
```bash
# Option 1: Use the automated script (Recommended)
./start-dev.sh

# Option 2: Manual startup
# Terminal 1 - Start API
cd BB.Api
dotnet run

# Terminal 2 - Start Web App
cd BB.Web
dotnet run
```

### 5. Access the Application
- **📊 Dashboard**: http://localhost:5084/dashboard
- **🧪 API Test**: http://localhost:5084/api-test
- **📖 API Docs**: http://localhost:5000/swagger

## 📋 Configuration

### BB.Api/appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=bb;User Id=sa;Password=YourPassword;TrustServerCertificate=True;"
  },
  "Bitbucket": {
    "ApiBaseUrl": "https://api.bitbucket.org/2.0/",
    "ConsumerKey": "your-consumer-key",
    "ConsumerSecret": "your-consumer-secret"
  }
}
```

### BB.Web/appsettings.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ApiBaseUrl": "http://localhost:5000"
}
```

## 📊 Data Synchronization

Before using the dashboard, you need to sync data from Bitbucket:

### 1. Sync Repositories
```bash
# Sync all repositories for a workspace
POST /api/sync/repositories/{workspace}
```

### 2. Sync Users
```bash
# Sync workspace members
POST /api/sync/users/{workspace}
```

### 3. Sync Commits
```bash
# Sync commits for a specific repository
POST /api/sync/commits/{workspace}/{repoSlug}
Content-Type: application/json

{
  "startDate": "2019-01-01T00:00:00",
  "endDate": "2019-12-31T23:59:59"
}
```

### 4. Sync Pull Requests
```bash
# Sync pull requests for a repository
POST /api/sync/pullrequests/{workspace}/{repoSlug}
Content-Type: application/json

{
  "startDate": "2019-01-01T00:00:00",
  "endDate": "2019-12-31T23:59:59"
}
```

## 🌐 Deployment

### Local Production Build
```bash
# Build API
cd BB.Api
dotnet publish -c Release -o ./publish

# Build Web App
cd BB.Web
dotnet publish -c Release -o ./publish
```

### Docker Deployment
Create `Dockerfile` for BB.Api:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["BB.Api/BB.Api.csproj", "BB.Api/"]
COPY ["BBIntegration/BBIntegration.csproj", "BBIntegration/"]
RUN dotnet restore "BB.Api/BB.Api.csproj"
COPY . .
WORKDIR "/src/BB.Api"
RUN dotnet build "BB.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BB.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BB.Api.dll"]
```

### Cloud Deployment Options

#### Azure App Service
1. **Create App Service** for both BB.Api and BB.Web
2. **Configure Connection Strings** in Application Settings
3. **Deploy** using Visual Studio, GitHub Actions, or Azure CLI

#### AWS/Other Clouds
1. Use appropriate .NET hosting services
2. Configure environment variables for database and Bitbucket settings
3. Ensure proper networking between API and Web components

### Environment Variables
For production deployment, use environment variables:
```bash
ConnectionStrings__DefaultConnection="your-production-db-connection"
Bitbucket__ConsumerKey="your-production-key"
Bitbucket__ConsumerSecret="your-production-secret"
```

## 🔧 Development

### Running Tests
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Adding New Features
1. **API Endpoints**: Add to `BB.Api/Endpoints/`
2. **UI Components**: Add to `BB.Web/Components/`
3. **Bitbucket Integration**: Extend `BBIntegration/`

### Debugging
- **API Logs**: Use built-in logging or check `api.log`
- **Web Logs**: Check browser console and `web.log`
- **Database**: Use SQL Server Management Studio or Azure Data Studio

## 📈 API Endpoints

### Analytics Endpoints
- `GET /api/analytics/repositories` - List all repositories
- `GET /api/analytics/commits/activity` - Commit activity data
- `GET /api/analytics/contributors` - Contributor activity data
- `GET /api/analytics/commits/punchcard` - Commit timing patterns

### Sync Endpoints
- `POST /api/sync/users/{workspace}` - Sync workspace users
- `POST /api/sync/repositories/{workspace}` - Sync repositories
- `POST /api/sync/commits/{workspace}/{repo}` - Sync commits
- `POST /api/sync/pullrequests/{workspace}/{repo}` - Sync pull requests

## 🔒 Security Notes

- **API Keys**: Store Bitbucket credentials securely
- **Database**: Use strong passwords and encrypted connections
- **CORS**: Configured for development ports (5084, 7051)
- **Authentication**: Consider adding OAuth for production use

## 🐛 Troubleshooting

### Common Issues

**"No Repositories Found"**
1. Check API connection on `/api-test` page
2. Verify database has repository data
3. Ensure sync process completed successfully

**Chart Not Loading**
1. Check browser console for JavaScript errors
2. Verify Chart.js CDN is accessible
3. Check data format in network tab

**API Connection Errors**
1. Ensure BB.Api is running on port 5000
2. Check CORS configuration
3. Verify `ApiBaseUrl` in BB.Web settings

**Database Connection Issues**
1. Verify SQL Server is running
2. Check connection string format
3. Ensure database and schema exist

### Log Files
When using `start-dev.sh`:
- **API Logs**: `tail -f api.log`
- **Web Logs**: `tail -f web.log`

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

---

**Made with ❤️ using .NET 9, Blazor, and Chart.js** 