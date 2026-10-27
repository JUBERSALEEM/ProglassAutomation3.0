using System.Windows;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("step1.d.ThemeTests")]
// The step2 and step3 verification executables open the ERP file themselves to
// assert what a migration actually did. Both use the database helpers marked
// internal for exactly that purpose; without these two lines those projects do
// not compile (CS0122), which no runtime test would ever reach.
[assembly: InternalsVisibleTo("step2.f.ClientTests")]
[assembly: InternalsVisibleTo("step3.f.UserTests")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
