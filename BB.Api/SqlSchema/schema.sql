-- Users table
CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BitbucketUserId NVARCHAR(100) NOT NULL UNIQUE,
    DisplayName NVARCHAR(255),
    AvatarUrl NVARCHAR(512),
    CreatedOn DATETIME,
    -- Add more user fields as needed
);

-- Repositories table
CREATE TABLE Repositories (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BitbucketRepoId NVARCHAR(100) NOT NULL UNIQUE,
    Name NVARCHAR(255) NOT NULL,
    FullName NVARCHAR(255),
    Workspace NVARCHAR(255),
    CreatedOn DATETIME,
    -- Add more repository fields as needed
);

-- Commits table
CREATE TABLE Commits (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BitbucketCommitHash NVARCHAR(50) NOT NULL UNIQUE,
    RepositoryId INT NOT NULL,
    AuthorId INT NOT NULL,
    Date DATETIME NOT NULL,
    Message NVARCHAR(MAX),
    LinesAdded INT,
    LinesRemoved INT,
    IsMerge BIT NOT NULL DEFAULT 0,
    CodeLinesAdded INT,
    CodeLinesRemoved INT,
    -- Add more commit fields as needed
    FOREIGN KEY (RepositoryId) REFERENCES Repositories(Id),
    FOREIGN KEY (AuthorId) REFERENCES Users(Id)
);
CREATE INDEX IX_Commits_RepositoryId ON Commits(RepositoryId);
CREATE INDEX IX_Commits_AuthorId ON Commits(AuthorId);
CREATE INDEX IX_Commits_Date ON Commits(Date);

-- PullRequests table
CREATE TABLE PullRequests (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BitbucketPrId NVARCHAR(100) NOT NULL UNIQUE,
    RepositoryId INT NOT NULL,
    AuthorId INT NOT NULL,
    Title NVARCHAR(512),
    State NVARCHAR(50),
    CreatedOn DATETIME,
    UpdatedOn DATETIME,
    MergedOn DATETIME,
    -- Add more PR fields as needed
    FOREIGN KEY (RepositoryId) REFERENCES Repositories(Id),
    FOREIGN KEY (AuthorId) REFERENCES Users(Id)
);
CREATE INDEX IX_PullRequests_RepositoryId ON PullRequests(RepositoryId);
CREATE INDEX IX_PullRequests_AuthorId ON PullRequests(AuthorId);
CREATE INDEX IX_PullRequests_State ON PullRequests(State);

-- PullRequestCommits join table
CREATE TABLE PullRequestCommits (
    PullRequestId INT NOT NULL,
    CommitId INT NOT NULL,
    PRIMARY KEY (PullRequestId, CommitId),
    FOREIGN KEY (PullRequestId) REFERENCES PullRequests(Id),
    FOREIGN KEY (CommitId) REFERENCES Commits(Id)
);
CREATE INDEX IX_PullRequestCommits_PullRequestId ON PullRequestCommits(PullRequestId);
CREATE INDEX IX_PullRequestCommits_CommitId ON PullRequestCommits(CommitId);
