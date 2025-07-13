-- SQL Migration Script for Adding Pull Request Details

-- Add ClosedOn column to PullRequests table
ALTER TABLE PullRequests
ADD ClosedOn DATETIME2;

-- Create PullRequestApprovals table
CREATE TABLE PullRequestApprovals (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PullRequestId INT NOT NULL,
    UserUuid NVARCHAR(255) NOT NULL,
    DisplayName NVARCHAR(255) NOT NULL,
    Role NVARCHAR(50), -- e.g., 'REVIEWER', 'PARTICIPANT'
    Approved BIT NOT NULL, -- True if approved, False if not (e.g., changes requested)
    State NVARCHAR(50), -- e.g., 'approved', 'changes_requested', 'needs_work'
    ApprovedOn DATETIME2, -- Timestamp of the approval
    FOREIGN KEY (PullRequestId) REFERENCES PullRequests(Id),
    UNIQUE (PullRequestId, UserUuid)
);

-- Create Indexes for PullRequestApprovals table
CREATE INDEX IX_PullRequestApprovals_PullRequestId ON PullRequestApprovals(PullRequestId);
CREATE INDEX IX_PullRequestApprovals_UserUuid ON PullRequestApprovals(UserUuid);
CREATE INDEX IX_PullRequestApprovals_Approved ON PullRequestApprovals(Approved); 