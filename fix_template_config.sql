-- Run once after any batch import (IWPM_BATCH_FILE, RESTRUCTURE_FOLDERS_FILE,
-- or LONGFORM_BATCH_FILE). Neither raw UMT nor IWebPageManager.Create()
-- populates ContentItemCommonDataVisualBuilderTemplateConfiguration
-- automatically; without it every request to the page throws:
--   InvalidOperationException: The view on path '' could not be found and there
--   is no page template registered for selected page.
-- Safe to re-run any time -- only touches rows still NULL.
-- Covers all three AUA page content types now in use:
--   AUA.StandardContentPage      (E9817886-B3F4-43C8-B3D9-F5520D3E1DE3)
--   AUA.ClinicalGuidanceDocument (AE033F69-30DD-4207-A582-3D4692AAC273)
--   AUA.LongFormDocumentPage     (9730BD4B-7079-478E-B51C-25E5631B3208)

UPDATE ccd
SET ContentItemCommonDataVisualBuilderTemplateConfiguration = '{"identifier":"AUA.StandardContentPage.Default"}'
FROM CMS_ContentItemCommonData ccd
JOIN CMS_ContentItem ci ON ci.ContentItemID = ccd.ContentItemCommonDataContentItemID
JOIN CMS_Class cl ON ci.ContentItemContentTypeID = cl.ClassID
WHERE cl.ClassGUID = 'E9817886-B3F4-43C8-B3D9-F5520D3E1DE3'
  AND ccd.ContentItemCommonDataVisualBuilderTemplateConfiguration IS NULL;

UPDATE ccd
SET ContentItemCommonDataVisualBuilderTemplateConfiguration = '{"identifier":"AUA.ClinicalGuidanceDocument.Default"}'
FROM CMS_ContentItemCommonData ccd
JOIN CMS_ContentItem ci ON ci.ContentItemID = ccd.ContentItemCommonDataContentItemID
JOIN CMS_Class cl ON ci.ContentItemContentTypeID = cl.ClassID
WHERE cl.ClassGUID = 'AE033F69-30DD-4207-A582-3D4692AAC273'
  AND ccd.ContentItemCommonDataVisualBuilderTemplateConfiguration IS NULL;

UPDATE ccd
SET ContentItemCommonDataVisualBuilderTemplateConfiguration = '{"identifier":"AUA.LongFormDocumentPage.Default"}'
FROM CMS_ContentItemCommonData ccd
JOIN CMS_ContentItem ci ON ci.ContentItemID = ccd.ContentItemCommonDataContentItemID
JOIN CMS_Class cl ON ci.ContentItemContentTypeID = cl.ClassID
WHERE cl.ClassGUID = '9730BD4B-7079-478E-B51C-25E5631B3208'
  AND ccd.ContentItemCommonDataVisualBuilderTemplateConfiguration IS NULL;
