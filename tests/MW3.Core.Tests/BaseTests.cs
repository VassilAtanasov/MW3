using System.Reflection;

namespace MW3.Core.Tests;

public class BaseTests
{
    [Fact]
    public void PublicSurface_ExposesOnlyTheAgreedMembers_NoneSettableFromOutsideAssembly()
    {
        var properties = typeof(Base).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyNames = properties.Select(p => p.Name).OrderBy(name => name).ToArray();

        Assert.Equal(
            new[]
            {
                nameof(Base.GarrisonCap),
                nameof(Base.GarrisonCount),
                nameof(Base.Id),
                nameof(Base.Level),
                nameof(Base.Owner),
                nameof(Base.Position),
                nameof(Base.ProductionProgressTicks),
            },
            propertyNames);

        foreach (var property in properties)
        {
            var setter = property.GetSetMethod(nonPublic: false);
            Assert.Null(setter);
        }
    }

    [Fact]
    public void OwnerType_IsNullable_SoNeutralIsAbsenceOfOwnerNotASentinel()
    {
        var ownerProperty = typeof(Base).GetProperty(nameof(Base.Owner))!;

        Assert.True(Nullable.GetUnderlyingType(ownerProperty.PropertyType) is not null
            || !ownerProperty.PropertyType.IsValueType);
    }
}
