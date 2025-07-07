-- File Classification Migration Script
-- Run this on existing database to add file classification support

-- Add new columns to existing Commits table
ALTER TABLE Commits ADD DataLinesAdded INT NOT NULL DEFAULT 0;
ALTER TABLE Commits ADD DataLinesRemoved INT NOT NULL DEFAULT 0;
ALTER TABLE Commits ADD ConfigLinesAdded INT NOT NULL DEFAULT 0;
ALTER TABLE Commits ADD ConfigLinesRemoved INT NOT NULL DEFAULT 0;
ALTER TABLE Commits ADD DocsLinesAdded INT NOT NULL DEFAULT 0;
ALTER TABLE Commits ADD DocsLinesRemoved INT NOT NULL DEFAULT 0;

-- Create CommitFiles table for detailed file-level tracking
CREATE TABLE CommitFiles (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CommitId INT NOT NULL,
    FilePath NVARCHAR(500) NOT NULL,
    FileType NVARCHAR(20) NOT NULL, -- 'code', 'data', 'config', 'docs', 'other'
    ChangeStatus NVARCHAR(20) NOT NULL, -- 'added', 'modified', 'removed'
    LinesAdded INT NOT NULL DEFAULT 0,
    LinesRemoved INT NOT NULL DEFAULT 0,
    FileExtension NVARCHAR(50),
    CreatedOn DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    FOREIGN KEY (CommitId) REFERENCES Commits(Id) ON DELETE CASCADE
);

-- Create indexes for performance
CREATE INDEX IX_CommitFiles_CommitId ON CommitFiles(CommitId);
CREATE INDEX IX_CommitFiles_FileType ON CommitFiles(FileType);
CREATE INDEX IX_CommitFiles_ChangeStatus ON CommitFiles(ChangeStatus);
CREATE INDEX IX_CommitFiles_FileExtension ON CommitFiles(FileExtension);
CREATE INDEX IX_Commits_DataLines ON Commits(DataLinesAdded, DataLinesRemoved);
CREATE INDEX IX_Commits_ConfigLines ON Commits(ConfigLinesAdded, ConfigLinesRemoved);
CREATE INDEX IX_Commits_DocsLines ON Commits(DocsLinesAdded, DocsLinesRemoved);

-- Verification query to check new columns
-- SELECT TOP 5 
--     Id, BitbucketCommitHash, 
--     LinesAdded, LinesRemoved,
--     CodeLinesAdded, CodeLinesRemoved,
--     DataLinesAdded, DataLinesRemoved,
--     ConfigLinesAdded, ConfigLinesRemoved,
--     DocsLinesAdded, DocsLinesRemoved
-- FROM Commits; 