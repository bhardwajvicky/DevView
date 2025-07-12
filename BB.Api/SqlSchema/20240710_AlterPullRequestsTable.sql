-- SQL Migration Script: Alter PullRequests Table for Composite Unique Key
--
-- This script updates the PullRequests table to enforce a unique constraint
-- on the combination of RepositoryId and BitbucketPrId.
--
-- IMPORTANT:
-- Before running this script, you MUST perform Step 1 to find the name of the existing unique
-- constraint on the 'BitbucketPrId' column.
-- Then, you must manually edit this file and paste that constraint name into the
-- 'ALTER TABLE PullRequests DROP CONSTRAINT' line in Step 2.1.

-- Step 1: Query to find existing unique constraint name
-- Execute this query in your SQL Server management tool and copy the 'constraint_name':
/*
SELECT
    tc.constraint_name,
    kcu.column_name
FROM
    information_schema.table_constraints AS tc
JOIN
    information_schema.key_column_usage AS kcu
ON
    tc.constraint_name = kcu.constraint_name
WHERE
    tc.table_name = 'PullRequests'
    AND tc.constraint_type = 'UNIQUE'
    AND kcu.column_name = 'BitbucketPrId';
*/
-- Example constraint name: UQ__PullRequ__5A3E54C2B21C7D3F (Your actual name will be different)

-- Step 2: Execute the migration commands

-- Step 2.1: Drop the existing auto-generated unique constraint on BitbucketPrId
-- PASTE THE CONSTRAINT NAME YOU FOUND IN STEP 1 HERE:
ALTER TABLE PullRequests
DROP CONSTRAINT [YourActualConstraintNameGoesHere]; -- <-- IMPORTANT: Replace this placeholder!

-- Step 2.2: Add the new composite unique constraint on (RepositoryId, BitbucketPrId)
-- This ensures uniqueness for a PR within a specific repository, which aligns with Bitbucket's model.
ALTER TABLE PullRequests
ADD CONSTRAINT UQ_PullRequests_RepositoryId_BitbucketPrId UNIQUE (RepositoryId, BitbucketPrId);

-- Add IsRevert column to Commits table
ALTER TABLE Commits
ADD IsRevert bit NOT NULL DEFAULT 0;

-- After running this script, re-run your sync process to ensure all PRs,
-- approvals, and commits are correctly associated. 