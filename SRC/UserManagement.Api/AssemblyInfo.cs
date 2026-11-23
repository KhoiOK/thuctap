using System.Runtime.CompilerServices;

// The OCR and Cloudinary helpers expose a few internal pure functions (label
// normalization, request signing) that are valuable to unit-test directly.
[assembly: InternalsVisibleTo("UserManagement.Api.Tests")]
[assembly: InternalsVisibleTo("UserManagement.Web.Tests")]
