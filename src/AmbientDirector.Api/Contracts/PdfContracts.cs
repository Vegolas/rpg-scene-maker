namespace AmbientDirector.Api.Contracts;

// Wire DTOs for PDF page → image import (issue #88). The uploaded PDF is a short-lived temp keyed by
// <c>Id</c>; the GM browses page thumbnails, picks pages, and the server renders them into ordinary stored
// images. No PDF is ever persisted (see PdfImporter). The UI keeps its own copies in its Contracts/.

/// <summary>Result of POST /images/pdf/upload: the temp id to browse/import against and the page count.</summary>
public record PdfUploadResultDto(string Id, int Pages);

/// <summary>Body of POST /images/pdf/{id}/import: the 1-based page numbers to render and store. The API
/// takes a list (Phase 2 boards import several at once); the current picker sends exactly one.</summary>
public record PdfImportRequest(List<int>? Pages);

/// <summary>One imported page: its 1-based <c>Page</c> and the stored image file name (<c>Id</c>), the same
/// id the upload/search-import flows return and that goes straight into an entity's Image or /tv/show.</summary>
public record PdfImportedPageDto(int Page, string Id);
