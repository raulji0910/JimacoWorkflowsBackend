using System.Runtime.CompilerServices;

namespace Jimaco.Aprobaciones.TestUnitarios;

internal static class ModuleInit
{
    // QuestPDF exige fijar el tipo de licencia antes de generar cualquier documento — en la Api
    // esto pasa en Program.cs; acá lo hacemos una sola vez para toda la suite de tests.
    [ModuleInitializer]
    public static void Inicializar() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
}
