using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.Tests.Services;

[TestClass]
public sealed class EventAccessCodeStoreTests
{
    [TestMethod]
    public void Get_ReturnsNullForAnUnknownEvent()
    {
        var store = new EventAccessCodeStore(new MockPreferencesService());

        Assert.IsNull(store.Get(42));
    }

    [TestMethod]
    public void SetThenGet_RoundTrips()
    {
        var store = new EventAccessCodeStore(new MockPreferencesService());
        store.Set(42, "1234");

        Assert.AreEqual("1234", store.Get(42));
    }

    [TestMethod]
    public void Codes_AreKeptPerEvent()
    {
        var store = new EventAccessCodeStore(new MockPreferencesService());
        store.Set(1, "1111");
        store.Set(2, "2222");

        Assert.AreEqual("1111", store.Get(1));
        Assert.AreEqual("2222", store.Get(2));
    }

    [TestMethod]
    public void Set_OverwritesAnExistingCode()
    {
        var store = new EventAccessCodeStore(new MockPreferencesService());
        store.Set(1, "1111");
        store.Set(1, "9999");

        Assert.AreEqual("9999", store.Get(1));
    }

    [TestMethod]
    public void Clear_RemovesOnlyTheNamedEvent()
    {
        var store = new EventAccessCodeStore(new MockPreferencesService());
        store.Set(1, "1111");
        store.Set(2, "2222");

        store.Clear(1);

        Assert.IsNull(store.Get(1));
        Assert.AreEqual("2222", store.Get(2));
    }

    [TestMethod]
    public void Clear_OnAnUnknownEventIsHarmless()
    {
        var store = new EventAccessCodeStore(new MockPreferencesService());
        store.Set(1, "1111");

        store.Clear(99);

        Assert.AreEqual("1111", store.Get(1));
    }

    [TestMethod]
    public void Codes_PersistAcrossInstances()
    {
        // The point of the store: the viewer enters a private event's code once per device.
        var preferences = new MockPreferencesService();
        new EventAccessCodeStore(preferences).Set(7, "7654321");

        var reloaded = new EventAccessCodeStore(preferences);

        Assert.AreEqual("7654321", reloaded.Get(7));
    }

    [TestMethod]
    public void Clear_PersistsAcrossInstances()
    {
        var preferences = new MockPreferencesService();
        var store = new EventAccessCodeStore(preferences);
        store.Set(7, "7654321");
        store.Clear(7);

        Assert.IsNull(new EventAccessCodeStore(preferences).Get(7));
    }

    [TestMethod]
    public void CorruptStoredJson_DegradesToAnEmptyStore()
    {
        var preferences = new MockPreferencesService();
        preferences.Set("PrivateEventAccessCodes", "{not json");

        var store = new EventAccessCodeStore(preferences);

        Assert.IsNull(store.Get(1));
        // And it recovers - a later write still works.
        store.Set(1, "1234");
        Assert.AreEqual("1234", store.Get(1));
    }
}
