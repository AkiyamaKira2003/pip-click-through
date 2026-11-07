//! Local-only, current-user-restricted named-pipe protocol compatible with pipctl.ps1.
use crate::engine::wide;
use serde::Deserialize;
use serde_json::json;
use std::{
    ffi::c_void,
    mem::size_of,
    ptr::null_mut,
    sync::mpsc::{self, Sender},
    time::Duration,
};
use windows_sys::Win32::{
    Foundation::{
        CloseHandle, ERROR_PIPE_CONNECTED, GetLastError, INVALID_HANDLE_VALUE, LocalFree,
    },
    Security::Authorization::{
        ConvertSidToStringSidW, ConvertStringSecurityDescriptorToSecurityDescriptorW,
        SDDL_REVISION_1,
    },
    Security::{
        GetTokenInformation, PSECURITY_DESCRIPTOR, SECURITY_ATTRIBUTES, TOKEN_QUERY, TOKEN_USER,
        TokenUser,
    },
    Storage::FileSystem::{FlushFileBuffers, PIPE_ACCESS_DUPLEX, ReadFile, WriteFile},
    System::{
        Pipes::{
            ConnectNamedPipe, CreateNamedPipeW, DisconnectNamedPipe, PIPE_READMODE_BYTE,
            PIPE_REJECT_REMOTE_CLIENTS, PIPE_TYPE_BYTE, PIPE_WAIT,
        },
        Threading::{GetCurrentProcess, OpenProcessToken},
    },
    UI::WindowsAndMessaging::PostMessageW,
};

pub const WM_CLI: u32 = 0x8000 + 27;
const PIPE: &str = r"\\.\pipe\PiPClickThrough.Control.v1";

pub struct Call {
    pub command: String,
    pub arg: String,
    pub respond: Sender<String>,
}
#[derive(Deserialize)]
struct Input {
    #[serde(default, alias = "Command")]
    command: String,
    #[serde(default, alias = "Arg")]
    arg: String,
}
fn response(s: &str) -> String {
    json!({"ok":false, "error":s}).to_string()
}

/// Returns Windows SDDL restricting the named pipe to this logged-in user.
fn current_sid() -> Result<String, String> {
    unsafe {
        let mut token = null_mut();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) == 0 {
            return Err(format!("OpenProcessToken: {}", GetLastError()));
        }
        let mut needed = 0;
        GetTokenInformation(token, TokenUser, null_mut(), 0, &mut needed);
        if needed < size_of::<TOKEN_USER>() as u32 {
            CloseHandle(token);
            return Err("Invalid user SID".into());
        }
        let mut bytes = vec![0u8; needed as usize];
        let ok = GetTokenInformation(
            token,
            TokenUser,
            bytes.as_mut_ptr().cast(),
            needed,
            &mut needed,
        );
        CloseHandle(token);
        if ok == 0 {
            return Err(format!("GetTokenInformation: {}", GetLastError()));
        }
        let info = &*(bytes.as_ptr() as *const TOKEN_USER);
        let mut sid = null_mut();
        if ConvertSidToStringSidW(info.User.Sid, &mut sid) == 0 {
            return Err(format!("ConvertSidToStringSid: {}", GetLastError()));
        }
        let mut len = 0usize;
        while *sid.add(len) != 0 {
            len += 1;
        }
        let out = String::from_utf16_lossy(std::slice::from_raw_parts(sid, len));
        LocalFree(sid.cast());
        Ok(out)
    }
}

pub fn start(hwnd: isize) -> Result<(), String> {
    let sid = current_sid()?;
    // Deny all users except current logon SID; block remote clients too.
    let sddl = wide(&format!("D:P(A;;GA;;;{sid})"));
    let mut descriptor: PSECURITY_DESCRIPTOR = null_mut();
    if unsafe {
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl.as_ptr(),
            SDDL_REVISION_1,
            &mut descriptor,
            null_mut(),
        )
    } == 0
    {
        return Err(format!("Cannot secure control pipe: {}", unsafe {
            GetLastError()
        }));
    }
    // Each accept uses the security descriptor, whose lifetime belongs to this thread.
    let descriptor_address = descriptor as isize;
    std::thread::spawn(move || unsafe {
        let descriptor = descriptor_address as PSECURITY_DESCRIPTOR;
        let attributes = SECURITY_ATTRIBUTES {
            nLength: size_of::<SECURITY_ATTRIBUTES>() as u32,
            lpSecurityDescriptor: descriptor,
            bInheritHandle: 0,
        };
        let path = wide(PIPE);
        loop {
            let pipe = CreateNamedPipeW(
                path.as_ptr(),
                PIPE_ACCESS_DUPLEX,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                1,
                8192,
                8192,
                0,
                &attributes,
            );
            if pipe == INVALID_HANDLE_VALUE {
                break;
            }
            let connected =
                ConnectNamedPipe(pipe, null_mut()) != 0 || GetLastError() == ERROR_PIPE_CONNECTED;
            if connected {
                let reply = match read_command(pipe) {
                    Ok(input) => {
                        let (tx, rx) = mpsc::channel();
                        let request = Box::new(Call {
                            command: input.command,
                            arg: input.arg,
                            respond: tx,
                        });
                        let ptr = Box::into_raw(request);
                        if PostMessageW(hwnd as _, WM_CLI, 0, ptr as isize) == 0 {
                            drop(Box::from_raw(ptr));
                            response("Window is closing")
                        } else {
                            rx.recv_timeout(Duration::from_secs(5))
                                .unwrap_or_else(|_| response("Controller timed out"))
                        }
                    }
                    Err(e) => response(&e),
                };
                let bytes = format!("{reply}\n");
                let mut written = 0u32;
                let _ = WriteFile(
                    pipe,
                    bytes.as_ptr().cast(),
                    bytes.len() as u32,
                    &mut written,
                    null_mut(),
                );
                // Do not discard the response until the connected CLI reads it.
                FlushFileBuffers(pipe);
                DisconnectNamedPipe(pipe);
            }
            CloseHandle(pipe);
        }
        LocalFree(descriptor);
    });
    Ok(())
}
unsafe fn read_command(pipe: *mut c_void) -> Result<Input, String> {
    let mut collected = Vec::new();
    let mut buf = [0u8; 256];
    loop {
        let mut n = 0;
        if unsafe {
            ReadFile(
                pipe,
                buf.as_mut_ptr().cast(),
                buf.len() as u32,
                &mut n,
                null_mut(),
            )
        } == 0
        {
            return Err("Control pipe read failed".into());
        }
        if n == 0 {
            return Err("Empty command".into());
        }
        collected.extend_from_slice(&buf[..n as usize]);
        if collected.len() > 4096 {
            return Err("Command too large".into());
        }
        if let Some(pos) = collected.iter().position(|b| *b == b'\n') {
            collected.truncate(pos);
            return serde_json::from_slice(&collected).map_err(|_| "Invalid command JSON".into());
        }
    }
}
