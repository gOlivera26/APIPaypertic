namespace PagoTicAPI.Tests.Integration.Controllers;

/// <summary>
/// Define la colección "IntegrationTests" para que los tests de integración
/// se ejecuten secuencialmente (evita conflictos con el mock compartido).
/// </summary>
[CollectionDefinition("IntegrationTests")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestBase> { }
