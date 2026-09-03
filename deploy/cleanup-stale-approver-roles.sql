-- ============================================================
-- OutsourceRequestApp — clean up stale ApproverRole rows
--
-- The current workflow only ever uses 5 approval roles:
--   WP, PROD, BUYER, SOURCING, MD
-- A previous version of the app used a different role structure
-- (see git history: "Rebuild approval workflow as a full 5-stage
-- chain"), and at least one leftover row — RoleDisplayName
-- "Business Systems Intern" — was never removed. The app code no
-- longer treats any row outside the 5 canonical keys as a real
-- approver role (see RequestStatus.ApproverRoleKeys /
-- AccessControlService.GetMyApproverRoleAsync), so this is now
-- inert either way, but it is still confusing to leave sitting in
-- the table and worth clearing out.
--
-- Run the SELECT first to see exactly what would be removed before
-- running the DELETE.
-- ============================================================

USE CSM_OutsourceRequests;
GO

-- 1) See every row that isn't one of the 5 roles the app recognises.
SELECT * FROM dbo.ApproverRoles
WHERE RoleKey NOT IN ('WP', 'PROD', 'BUYER', 'SOURCING', 'MD');

-- 2) Also check for accidental DUPLICATES of a real role key (e.g. two rows
--    both keyed "WP") — these can't happen through the Admin panel (it
--    upserts by RoleKey), only via a direct SQL edit. If this returns any
--    rows, inspect them manually before deciding which one to keep —
--    the cleanup DELETE below only removes non-canonical keys, not
--    duplicates of a valid one.
SELECT RoleKey, COUNT(*) AS RowCount
FROM dbo.ApproverRoles
GROUP BY RoleKey
HAVING COUNT(*) > 1;

-- 3) Once you've confirmed the rows from step 1 are the ones to remove,
--    uncomment and run this:
-- DELETE FROM dbo.ApproverRoles
-- WHERE RoleKey NOT IN ('WP', 'PROD', 'BUYER', 'SOURCING', 'MD');
