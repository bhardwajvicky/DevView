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
    Slug NVARCHAR(255) NOT NULL,
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
    IsPRMergeCommit BIT NOT NULL DEFAULT 0,
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

-- Update script for existing DB
-- Add the new column
ALTER TABLE Commits ADD IsPRMergeCommit BIT NOT NULL DEFAULT 0;

-- Set IsMerge for commits whose message starts with 'merge' or 'Merged' (case-insensitive)
UPDATE Commits
SET IsMerge = 1
WHERE LOWER(LEFT(LTRIM(Message), 5)) = 'merge' OR LOWER(LEFT(LTRIM(Message), 6)) = 'merged';

-- Set IsPRMergeCommit for commits that are the merge_commit for a PR
UPDATE Commits
SET IsPRMergeCommit = 1
WHERE BitbucketCommitHash IN (
    SELECT pr.MergeCommitHash
    FROM (
        SELECT BitbucketPrId, (SELECT TOP 1 BitbucketCommitHash FROM Commits WHERE Commits.Message LIKE '%pull request%' AND Commits.Message LIKE '%' + CAST(PullRequests.BitbucketPrId AS NVARCHAR) + '%') AS MergeCommitHash
        FROM PullRequests
    ) pr
    WHERE pr.MergeCommitHash IS NOT NULL
);
-- You may need to adjust the above subquery depending on how you store PR merge commit hashes.
