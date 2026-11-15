namespace Sharp.Validator.LibraryFixtures;

public sealed class LoadModelValidator() : Validator<LoadModel>(builder => builder.Property(x => x.Name).NotEmpty());