using System.Windows;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("step1.d.ThemeTests")]
// The step2, step3 and step4 verification executables open the ERP file
// themselves to assert what a migration actually did, so they call the database
// helpers marked internal for exactly that purpose. A missing line here does not
// fail a test — it stops the PROJECT compiling (CS0117/CS0122), and the only
// symptom is a build error that no runtime check would ever reach.
//
// Round 34.2: step4 was added to the repository without its line, so
// step4.f.CompanyTests could not build. Every verification executable is listed
// here, and step1.d.6 now fails if a project calls an internal helper without
// being named.
[assembly: InternalsVisibleTo("step2.f.ClientTests")]
[assembly: InternalsVisibleTo("step3.f.UserTests")]
[assembly: InternalsVisibleTo("step4.f.CompanyTests")]
// step5.f runs the real migration runner against a working copy; the connection
// policy it shares with the application is internal on purpose (docs/00 rule K).
[assembly: InternalsVisibleTo("step5.f.ProjectTests")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
