-- Backfills the new AccessLevel field from the existing MembersOnly checkbox
-- so pages that already have MembersOnly checked keep their gating instead
-- of silently reverting to Public. Run this AFTER adding the AccessLevel
-- field in the admin (step 1 of ACCESS_LEVEL_SETUP.md) and BEFORE anyone
-- relies on AccessLevel in the live templates.
--
-- Safe to re-run -- only touches rows where AccessLevel is still NULL/blank,
-- so running it twice (or running it again later after more pages get
-- MembersOnly checked by hand before this migration is finished everywhere)
-- won't overwrite anything already set.
--
-- MembersOnly = 1  -> AccessLevel = 'MembersOnly'
-- MembersOnly = 0  -> AccessLevel = 'Public'
-- (Nothing maps to 'RequiresLogin' here -- that's a brand new level with no
-- old-field equivalent; set it by hand afterward on whichever pages should
-- use it.)

UPDATE AUA_StandardContentPage
SET AccessLevel = CASE WHEN MembersOnly = 1 THEN 'MembersOnly' ELSE 'Public' END
WHERE AccessLevel IS NULL OR AccessLevel = '';

UPDATE AUA_LongFormDocumentPage
SET AccessLevel = CASE WHEN MembersOnly = 1 THEN 'MembersOnly' ELSE 'Public' END
WHERE AccessLevel IS NULL OR AccessLevel = '';

UPDATE AUA_ClinicalGuidanceDocument
SET AccessLevel = CASE WHEN MembersOnly = 1 THEN 'MembersOnly' ELSE 'Public' END
WHERE AccessLevel IS NULL OR AccessLevel = '';

-- Verification -- run this after the updates above and eyeball it before
-- moving on. Expect ZERO rows where MembersOnly = 1 but AccessLevel isn't
-- 'MembersOnly' (that would mean a page's real gating got lost).
SELECT 'AUA_StandardContentPage' AS TableName, COUNT(*) AS MismatchCount
FROM AUA_StandardContentPage
WHERE MembersOnly = 1 AND (AccessLevel IS NULL OR AccessLevel <> 'MembersOnly')
UNION ALL
SELECT 'AUA_LongFormDocumentPage', COUNT(*)
FROM AUA_LongFormDocumentPage
WHERE MembersOnly = 1 AND (AccessLevel IS NULL OR AccessLevel <> 'MembersOnly')
UNION ALL
SELECT 'AUA_ClinicalGuidanceDocument', COUNT(*)
FROM AUA_ClinicalGuidanceDocument
WHERE MembersOnly = 1 AND (AccessLevel IS NULL OR AccessLevel <> 'MembersOnly');
