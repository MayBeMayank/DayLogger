namespace DayLogger.Services;

/// <summary>Reads a phone contact's photo, which MAUI's contact picker does not expose. Android only; elsewhere there is none.</summary>
public static class ContactPhoto
{
    public static byte[]? Read(string? contactId)
    {
#if ANDROID
        try
        {
            if (!long.TryParse(contactId, out var id)) return null;
            var resolver = Android.App.Application.Context.ContentResolver;
            var uri = Android.Content.ContentUris.WithAppendedId(Android.Provider.ContactsContract.Contacts.ContentUri!, id);
            using var stream = Android.Provider.ContactsContract.Contacts.OpenContactPhotoInputStream(resolver, uri, true);
            if (stream == null) return null;
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        catch { return null; } // a missing photo is normal; never block picking the contact
#else
        return null;
#endif
    }
}
