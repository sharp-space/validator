using System.Reflection;

namespace Sharp.Validator.Tests.Accessors;

// Property(x => x.Code) on a field threw InvalidCastException where it was declared: the selector check cast the member
// a chain ends at to PropertyInfo. A field in the middle of a chain or as a comparison target already worked, and the
// source generator already emits accessors for fields, so a field may now end a chain as a property does.
public sealed class FieldSelectorTests
{
    [Fact]
    public void Property_EndingAtAField_IsValidated()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Code).NotEmpty());
        Assert.Equal("Code", Assert.Single(validator.Validate(new Order { Code = "" }).Errors).PropertyName);
        Assert.True(validator.Validate(new Order { Code = "A1" }).IsValid);
    }

    [Fact]
    public void NestedProperty_EndingAtAField_SkipsANullParent()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Shipping!.City).NotEmpty());
        Assert.True(validator.Validate(new Order()).IsValid);
        var error = Assert.Single(validator.Validate(new Order { Shipping = new Address { City = "" } }).Errors);
        Assert.Equal("Shipping.City", error.PropertyName);
    }

    [Fact]
    public void Elements_OfAField_ValidatesEachItem()
    {
        var validator = new Validator<Order>(x => x.Elements(order => order.Lines).NotEmpty());
        Assert.Equal("Lines[1]", Assert.Single(validator.Validate(new Order { Lines = ["a", ""] }).Errors).PropertyName);
    }

    [Fact]
    public void IncludeProperty_OfAField_SelectsItsChain()
    {
        var validator = new Validator<Order>(x =>
        {
            x.Property(order => order.Code).NotEmpty();
            x.Property(order => order.Quantity).GreaterThan(0);
        });

        var result = validator.Validate(new Order { Code = "", Quantity = 0 },
            context => context.IncludeProperty(order => order.Quantity));

        Assert.Equal("Quantity", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void DisplayNameResolver_ReceivesTheField()
    {
        var validator = new Validator<Order>(x => x.Property(order => order.Code).NotEmpty());
        validator.Configure(x => x.DisplayNameResolver = member => member is FieldInfo ? "Order code" : null);
        Assert.Equal("'Order code' must not be empty.", Assert.Single(validator.Validate(new Order()).Errors).Message);
    }

    private sealed class Address
    {
        public string? City;
    }

    private sealed class Order
    {
        public string? Code;
        public List<string?>? Lines;
        public int Quantity;
        public Address? Shipping;
    }
}