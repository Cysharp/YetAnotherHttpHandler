use std::error::Error;

fn main() -> Result<(), Box<dyn Error>> {
    //csbindgen::Builder::default()
    //    .input_extern_file("src/lib.rs")
    //    .input_extern_file("src/interop.rs")
    //    .csharp_namespace("Cysharp.Net.Http")
    //    .csharp_dll_name("Cysharp.Net.Http.YetAnotherHttpHandler.Native")
    //    .csharp_dll_name_if("UNITY_IOS && !UNITY_EDITOR", "__Internal")
    //    .csharp_use_function_pointer(false)
    //    .generate_csharp_file("../../src/YetAnotherHttpHandler/NativeMethods.g.cs")
    //    .unwrap();

    Ok(())
}
