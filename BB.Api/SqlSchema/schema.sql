-- Database schema for BitBucket Analytics

-- Users table
CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BitbucketUserId NVARCHAR(255) NOT NULL UNIQUE,
    DisplayName NVARCHAR(255) NOT NULL,
    AvatarUrl NVARCHAR(500),
    CreatedOn DATETIME2
);

-- Repositories table
CREATE TABLE Repositories (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BitbucketRepoId NVARCHAR(255),
    Name NVARCHAR(255) NOT NULL,
    Slug NVARCHAR(255) NOT NULL,
    Workspace NVARCHAR(255) NOT NULL,
    CreatedOn DATETIME2
);

-- Commits table
CREATE TABLE Commits (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BitbucketCommitHash NVARCHAR(255) NOT NULL UNIQUE,
    RepositoryId INT NOT NULL,
    AuthorId INT NOT NULL,
    Date DATETIME2 NOT NULL,
    Message NVARCHAR(MAX),
    LinesAdded INT DEFAULT 0,
    LinesRemoved INT DEFAULT 0,
    IsMerge BIT NOT NULL DEFAULT 0,
    CodeLinesAdded INT,
    CodeLinesRemoved INT,
    IsPRMergeCommit BIT NOT NULL DEFAULT 0,
    FOREIGN KEY (RepositoryId) REFERENCES Repositories(Id),
    FOREIGN KEY (AuthorId) REFERENCES Users(Id)
);

-- Pull Requests table
CREATE TABLE PullRequests (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BitbucketPrId NVARCHAR(255) NOT NULL UNIQUE,
    RepositoryId INT NOT NULL,
    AuthorId INT NOT NULL,
    Title NVARCHAR(MAX),
    State NVARCHAR(50),
    CreatedOn DATETIME2,
    UpdatedOn DATETIME2,
    MergedOn DATETIME2,
    FOREIGN KEY (RepositoryId) REFERENCES Repositories(Id),
    FOREIGN KEY (AuthorId) REFERENCES Users(Id)
);

-- Indexes for performance
CREATE INDEX IX_Commits_RepositoryId ON Commits(RepositoryId);
CREATE INDEX IX_Commits_AuthorId ON Commits(AuthorId);
CREATE INDEX IX_Commits_Date ON Commits(Date);
CREATE INDEX IX_Commits_BitbucketCommitHash ON Commits(BitbucketCommitHash);
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

-- DEPRECATED: Old message-based logic (kept for reference)
-- Set IsMerge for commits whose message starts with 'merge' or 'Merged' (case-insensitive)
-- UPDATE Commits
-- SET IsMerge = 1
-- WHERE LOWER(LEFT(LTRIM(Message), 5)) = 'merge' OR LOWER(LEFT(LTRIM(Message), 6)) = 'merged';

-- DEPRECATED: Old PR merge commit detection (kept for reference)
-- Set IsPRMergeCommit for commits that are the merge_commit for a PR
-- UPDATE Commits
-- SET IsPRMergeCommit = 1
-- WHERE BitbucketCommitHash IN (
--     SELECT pr.MergeCommitHash
--     FROM (
--         SELECT BitbucketPrId, (SELECT TOP 1 BitbucketCommitHash FROM Commits WHERE Commits.Message LIKE '%pull request%' AND Commits.Message LIKE '%' + CAST(PullRequests.BitbucketPrId AS NVARCHAR) + '%') AS MergeCommitHash
--         FROM PullRequests
--     ) pr
--     WHERE pr.MergeCommitHash IS NOT NULL
-- );

-- NEW LOGIC: Use parents array from Bitbucket API
-- IsMerge = true when commit has 2+ parents (merge commits)
-- IsPRMergeCommit = true when commit has 2+ parents (most merge commits are PR merges)

-- NOTE: The new logic is implemented in the sync services:
-- - BitbucketCommitsService.cs: Uses commit.Parents.Count >= 2 for both IsMerge and IsPRMergeCommit
-- - BitbucketPullRequestsService.cs: Uses commit.Parents.Count >= 2 for both IsMerge and IsPRMergeCommit
-- - Both flags are now set to the same value (true for merge commits, false for regular commits)
-- 
-- For existing data, you need to re-sync commits to get the correct parent information
-- from the Bitbucket API, as the parents array was not previously captured.
--
-- MIGRATION STEPS:
-- 1. Deploy the updated code with parents array support
-- 2. Re-sync all repositories to capture parent information and update flags
-- 3. The sync process will automatically set the correct IsMerge and IsPRMergeCommit values

-- Clean up incorrect data (optional - only if you want to reset before re-sync)
-- UPDATE Commits SET IsMerge = 0, IsPRMergeCommit = 0;

-- POST-SYNC FIX: Update IsPRMergeCommit for commits that are merge commits and associated with PRs
-- Run this AFTER both commit sync and PR sync are complete
-- This fixes the case where commit sync runs first and sets IsPRMergeCommit=0 for all commits
UPDATE Commits 
SET IsPRMergeCommit = 1
WHERE IsMerge = 1 
  AND Id IN (
      SELECT DISTINCT prc.CommitId 
      FROM PullRequestCommits prc
      INNER JOIN PullRequests pr ON prc.PullRequestId = pr.Id
  );

-- Verification query: Check the results
-- SELECT 
--     COUNT(*) as TotalCommits,
--     SUM(CASE WHEN IsMerge = 1 THEN 1 ELSE 0 END) as MergeCommits,
--     SUM(CASE WHEN IsPRMergeCommit = 1 THEN 1 ELSE 0 END) as PRMergeCommits,
--     SUM(CASE WHEN IsMerge = 1 AND IsPRMergeCommit = 0 THEN 1 ELSE 0 END) as MergeCommitsNotInPR
-- FROM Commits;
