namespace AmbientDirector.Ui.Contracts;

// Mirrors the API's PDF-import contracts (contracts are duplicated per project by design — keep in sync by
// hand). Backs PdfPagePicker (POST images/pdf/upload, GET images/pdf/{id}/thumb/{page}, POST images/pdf/{id}/import).

// Result of images/pdf/upload: the temp id to browse/import against and the number of pages.
public record PdfUploadResultDto(string Id, int Pages);

// One imported page: its 1-based Page and the stored image file name (Id) — the same id the upload/search
// flows return, which goes straight into an entity's Image or /tv/show.
public record PdfImportedPageDto(int Page, string Id);
