-- SQL Migration Script: Add IsRevert to PullRequests and ExcludeFromReporting to CommitFiles

-- Add IsRevert column to PullRequests table
ALTER TABLE PullRequests
ADD IsRevert BIT NOT NULL DEFAULT 0;

-- Add ExcludeFromReporting column to CommitFiles table
ALTER TABLE CommitFiles
ADD ExcludeFromReporting BIT NOT NULL DEFAULT 0;

-- Add indexes for performance
CREATE INDEX IX_PullRequests_IsRevert ON PullRequests(IsRevert);
CREATE INDEX IX_CommitFiles_ExcludeFromReporting ON CommitFiles(ExcludeFromReporting);

-- Update existing records (all set to false by default, which is already handled by DEFAULT 0)
-- No additional updates needed as the default values are appropriate 