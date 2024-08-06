use std::error::Error;

fn main() -> Result<(), Box<dyn Error>> {
    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .input_extern_file("src/binding.rs")
        .input_extern_file("src/interop.rs")
        .input_extern_file("src/context.rs")
        .input_extern_file("src/primitives.rs")
        .csharp_namespace("Cysharp.Net.Http")
        .csharp_dll_name("Cysharp.Net.Http.YetAnotherHttpHandler.Native")
        .csharp_dll_name_if("UNITY_IOS && !UNITY_EDITOR", "__Internal")
        .csharp_use_function_pointer(false)
        .csharp_file_header("#if !UNITY_WSA")
        .csharp_file_footer("#endif")
        .generate_csharp_file("../../src/YetAnotherHttpHandler/NativeMethods.g.cs")
        .unwrap();

    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .input_extern_file("src/binding.rs")
        .input_extern_file("src/interop.rs")
        .input_extern_file("src/context.rs")
        .input_extern_file("src/primitives.rs")
        .csharp_namespace("Cysharp.Net.Http")
        .csharp_dll_name("Cysharp.Net.Http.YetAnotherHttpHandler.Native.dll")
        .csharp_file_header("#if UNITY_WSA")
        .csharp_file_footer("#endif")
        .csharp_use_function_pointer(false)
        .generate_csharp_file("../../src/YetAnotherHttpHandler/NativeMethods.Uwp.g.cs")
        .unwrap();

    Ok(())
}
