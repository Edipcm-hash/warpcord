# ============================================================================
#  WarpCord core  -  shared engine
#  PowerShell 5.1 compatible, no external modules, no Python required.
#
#  Everything here is ASCII on purpose: Windows PowerShell 5.1 reads BOM-less
#  files as ANSI, and a Turkish "i" in a script is a very expensive bug.
# ============================================================================

$script:WarpCordVersion   = '2.0.0'
$script:WarpCordStateFile = 'state.json'
$script:WarpCordTaskName  = 'DiscordOnlyTunnel'
$script:WarpCordFragTask  = 'WarpCordFrag'
$script:WarpCordFirewall  = 'WarpCord'
$script:WarpCordLegacyFirewall = 'DiscordTunnel'
$script:WarpCordTunName   = 'WarpCord'

$script:DiscordSuffixes = @(
    'discord.com', 'discord.gg', 'discordapp.com', 'discordapp.net', 'discord.media',
    'discordstatus.com', 'discord.co', 'discordapp.io', 'discordsays.com', 'discordmerch.com'
)
$script:DiscordProcesses = @(
    'Discord.exe', 'DiscordPTB.exe', 'DiscordCanary.exe', 'DiscordDevelopment.exe', 'Update.exe'
)
$script:DiscordProcessRegex = '(?i).*[\\/]Discord[\\/].*'

# Cloudflare WARP anycast. The registration response tells us the closest one,
# but Turkish ISPs like to block individual addresses/ports, so we keep a
# fallback matrix and let the prober pick what actually works on this line.
$script:WarpEndpointIps = @(
    '162.159.192.1', '162.159.193.1', '162.159.195.1',
    '188.114.96.1', '188.114.97.1', '188.114.98.1', '188.114.99.1'
)
$script:WarpEndpointPorts = @(2408, 500, 1701, 4500)
$script:WarpEndpointV6 = @('2606:4700:d0::a29f:c001', '2606:4700:d0::a29f:c009')
$script:WarpPeerPublicKey = 'bmXOC+F1FxEMF9dyiK2H5/1SUtzH0JuVo51h2wPfgyo='

$script:WarpLogPath  = $null
$script:LogTitle = 'WarpCord'

# ----------------------------------------------------------------- logging --
function Initialize-WarpLog {
    param([string]$Path, [string]$Title = 'WarpCord')
    $script:WarpLogPath = $Path
    $script:LogTitle = $Title
    if ($Path) {
        $dir = Split-Path -Parent $Path
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        try {
            [IO.File]::WriteAllText($Path, "=== $Title $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===`r`n", (New-Object Text.UTF8Encoding($false)))
        } catch { $script:WarpLogPath = $null }
    }
}

function Write-WarpLog {
    param([string]$Message, [ValidateSet('INFO', 'OK', 'WARN', 'ERROR', 'STEP')][string]$Level = 'INFO')
    $color = switch ($Level) {
        'OK'    { 'Green' }
        'WARN'  { 'Yellow' }
        'ERROR' { 'Red' }
        'STEP'  { 'Cyan' }
        default { 'Gray' }
    }
    $prefix = switch ($Level) {
        'OK'    { '[ OK ]' }
        'WARN'  { '[WARN]' }
        'ERROR' { '[FAIL]' }
        'STEP'  { '====>' }
        default { '[ -- ]' }
    }
    $line = "$prefix $Message"
    Write-Host $line -ForegroundColor $color
    if ($script:WarpLogPath) {
        try {
            Add-Content -Path $script:WarpLogPath -Value ("{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $line) -Encoding UTF8
        } catch { }
    }
}

function Write-WarpSection {
    param([string]$Title)
    Write-Host ''
    Write-Host "--- $Title " -ForegroundColor DarkCyan -NoNewline
    Write-Host ('-' * [Math]::Max(0, 62 - $Title.Length)) -ForegroundColor DarkGray
    if ($script:WarpLogPath) {
        try { Add-Content -Path $script:WarpLogPath -Value "`r`n--- $Title ---" -Encoding UTF8 } catch { }
    }
}

# -------------------------------------------------------------- utilities --
function Initialize-WarpTls {
    # Windows PowerShell 5.1 defaults to TLS 1.0 on some builds; GitHub and
    # Cloudflare both refuse it. This is the single most common install failure.
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    } catch { }
    try {
        [Net.ServicePointManager]::Expect100Continue = $false
    } catch { }
}

function Test-WarpAdmin {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch {
        return $false
    }
}

function Get-WarpArchitecture {
    $arch = $env:PROCESSOR_ARCHITECTURE
    if ($env:PROCESSOR_ARCHITEW6432) { $arch = $env:PROCESSOR_ARCHITEW6432 }
    switch ($arch.ToUpperInvariant()) {
        'AMD64' { return 'amd64' }
        'ARM64' { return 'arm64' }
        'X86'   { return 'x86' }
        default { return 'unknown' }
    }
}

function Get-FreeTcpPort {
    param([int]$Preferred = 0)
    if ($Preferred -gt 0) {
        $listener = $null
        try {
            $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $Preferred)
            $listener.Start()
            return $Preferred
        } catch {
        } finally {
            if ($listener) { try { $listener.Stop() } catch { } }
        }
    }
    $probe = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    $probe.Start()
    $port = ([Net.IPEndPoint]$probe.LocalEndpoint).Port
    $probe.Stop()
    return $port
}

function Test-TcpPort {
    param([string]$ComputerName = '127.0.0.1', [int]$Port, [int]$TimeoutMs = 1000)
    $client = New-Object Net.Sockets.TcpClient
    try {
        $async = $client.BeginConnect($ComputerName, $Port, $null, $null)
        if (-not $async.AsyncWaitHandle.WaitOne($TimeoutMs)) { return $false }
        $client.EndConnect($async)
        return $true
    } catch {
        return $false
    } finally {
        try { $client.Close() } catch { }
    }
}

function Read-WarpHeaderBlock {
    param([System.IO.Stream]$Stream, [int]$MaxBytes = 8192)
    $buffer = New-Object byte[] 1
    $builder = New-Object Text.StringBuilder
    $total = 0
    while ($total -lt $MaxBytes) {
        $read = $Stream.Read($buffer, 0, 1)
        if ($read -le 0) { break }
        [void]$builder.Append([char]$buffer[0])
        $total++
        if ($builder.Length -ge 4 -and $builder.ToString($builder.Length - 4, 4) -eq "`r`n`r`n") { break }
    }
    return $builder.ToString()
}

function Invoke-WarpTlsProbe {
    # A deliberately dumb, fully bounded HTTPS probe: raw TCP (+ optional HTTP
    # CONNECT), TLS handshake, one GET, read to EOF. Every step has a socket
    # timeout, so it can never wedge the installer the way Invoke-WebRequest can.
    param(
        [string]$HostName = 'discord.com',
        [string]$Path = '/api/v9/gateway',
        [string]$ProxyHost = '',
        [int]$ProxyPort = 0,
        [int]$TimeoutSec = 10,
        [string]$Expect = 'gateway\.discord\.gg'
    )
    $result = [pscustomobject]@{ Ok = $false; Ms = -1; Detail = ''; Kind = '' }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $client = New-Object Net.Sockets.TcpClient
    $ssl = $null
    try {
        $client.NoDelay = $true
        $connectHost = $ProxyHost
        $connectPort = $ProxyPort
        if (-not $connectHost) { $connectHost = $HostName; $connectPort = 443 }
        $async = $client.BeginConnect($connectHost, $connectPort, $null, $null)
        if (-not $async.AsyncWaitHandle.WaitOne($TimeoutSec * 1000)) {
            $result.Detail = 'baglanti zaman asimi'
            $result.Kind = 'timeout'
            return $result
        }
        $client.EndConnect($async)
        $client.ReceiveTimeout = $TimeoutSec * 1000
        $client.SendTimeout = $TimeoutSec * 1000
        $stream = $client.GetStream()
        $stream.ReadTimeout = $TimeoutSec * 1000
        $stream.WriteTimeout = $TimeoutSec * 1000

        if ($ProxyHost) {
            $request = "CONNECT ${HostName}:443 HTTP/1.1`r`nHost: ${HostName}:443`r`nProxy-Connection: keep-alive`r`n`r`n"
            $bytes = [Text.Encoding]::ASCII.GetBytes($request)
            $stream.Write($bytes, 0, $bytes.Length)
            $header = Read-WarpHeaderBlock -Stream $stream
            if ($header -notmatch ' 200') {
                $line = ($header -split "`r`n")[0]
                $result.Detail = "proxy: $line"
                $result.Kind = 'proxy'
                return $result
            }
        }

        $ssl = New-Object Net.Security.SslStream($stream, $false)
        $ssl.ReadTimeout = $TimeoutSec * 1000
        $ssl.WriteTimeout = $TimeoutSec * 1000
        $ssl.AuthenticateAsClient($HostName)

        $get = "GET $Path HTTP/1.1`r`nHost: $HostName`r`nUser-Agent: WarpCord/$script:WarpCordVersion`r`nAccept: */*`r`nConnection: close`r`n`r`n"
        $getBytes = [Text.Encoding]::ASCII.GetBytes($get)
        $ssl.Write($getBytes, 0, $getBytes.Length)
        $reader = New-Object IO.StreamReader($ssl)
        $response = $reader.ReadToEnd()
        $result.Ms = $watch.ElapsedMilliseconds
        if ($response -match $Expect) {
            $result.Ok = $true
            $result.Kind = 'ok'
            $result.Detail = $response.Trim()
        } else {
            $clean = ($response -replace '\s+', ' ').Trim()
            if ($clean.Length -gt 120) { $clean = $clean.Substring(0, 120) + '...' }
            $result.Detail = $clean
        }
    } catch {
        $result.Ms = $watch.ElapsedMilliseconds
        $result.Detail = $_.Exception.Message
        $exception = $_.Exception
        if ($exception -is [System.Security.Authentication.AuthenticationException]) { $result.Kind = 'tls' }
        elseif ($exception -is [System.IO.IOException]) { $result.Kind = 'io' }
        elseif ($exception -is [System.Net.Sockets.SocketException]) { $result.Kind = 'socket' }
        else { $result.Kind = 'other' }
    } finally {
        if ($ssl) { try { $ssl.Dispose() } catch { } }
        try { $client.Close() } catch { }
    }
    return $result
}

function Write-WarpJsonFile {
    param([string]$Path, $Object)
    $json = $Object | ConvertTo-Json -Depth 40
    # UTF8 *without* BOM: sing-box (Go) rejects a BOM as "invalid character".
    [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding($false)))
}

function Read-WarpJsonFile {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $null }
    try {
        return (Get-Content -Path $Path -Raw -ErrorAction Stop | ConvertFrom-Json)
    } catch {
        return $null
    }
}

function Get-WarpState {
    param([string]$InstallDir)
    return (Read-WarpJsonFile (Join-Path $InstallDir $script:WarpCordStateFile))
}

function Save-WarpState {
    param([string]$InstallDir, $State)
    if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null }
    Write-WarpJsonFile (Join-Path $InstallDir $script:WarpCordStateFile) $State
}

function Set-WarpPrivateAcl {
    param([string]$Path)
    # The WARP private key is anonymous and free, but it is still a private key.
    # ProgramData is world-readable, so drop inheritance and keep SYSTEM, the
    # Administrators group and the current user (never lock the user out).
    if (-not (Test-Path $Path)) { return }
    $sid = ''
    try { $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value } catch { }
    $grants = @('*S-1-5-18:F', '*S-1-5-32-544:F')
    if ($sid) { $grants += "*$($sid):F" }
    try {
        & icacls $Path /inheritance:r /grant:r @grants 2>&1 | Out-Null
    } catch { }
}

# ----------------------------------------------------------------- crypto --
function Initialize-WarpCrypto {
    if ('WarpCordCrypto' -as [type]) { return $true }
    $source = @'
using System;
using System.Numerics;
using System.Security.Cryptography;

public static class WarpCordCrypto
{
    static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    static readonly BigInteger A24 = new BigInteger(121665);

    static BigInteger ToBig(byte[] b)
    {
        var t = new byte[b.Length + 1];
        Array.Copy(b, t, b.Length);
        return new BigInteger(t);
    }

    static byte[] To32(BigInteger v)
    {
        var raw = v.ToByteArray();
        var o = new byte[32];
        Array.Copy(raw, o, Math.Min(32, raw.Length));
        return o;
    }

    static void CSwap(BigInteger s, ref BigInteger a, ref BigInteger b)
    {
        BigInteger d = s * ((a - b) % P);
        a = (a - d) % P;
        b = (b + d) % P;
    }

    public static byte[] ScalarMult(byte[] kIn, byte[] uIn)
    {
        var k = new byte[32];
        Array.Copy(kIn, k, 32);
        k[0] &= 248; k[31] &= 127; k[31] |= 64;
        BigInteger kInt = new BigInteger(k);
        var u = new byte[32];
        Array.Copy(uIn, u, 32);
        u[31] &= 127;
        BigInteger x1 = ToBig(u);
        BigInteger x2 = BigInteger.One, z2 = BigInteger.Zero, x3 = x1, z3 = BigInteger.One;
        BigInteger swap = BigInteger.Zero;
        for (int t = 254; t >= 0; t--)
        {
            BigInteger kt = (kInt >> t) & BigInteger.One;
            swap ^= kt;
            CSwap(swap, ref x2, ref x3);
            CSwap(swap, ref z2, ref z3);
            swap = kt;
            BigInteger a = (x2 + z2) % P;
            BigInteger aa = (a * a) % P;
            BigInteger b = (x2 - z2) % P;
            BigInteger bb = (b * b) % P;
            BigInteger e = (aa - bb) % P;
            BigInteger c = (x3 + z3) % P;
            BigInteger d = (x3 - z3) % P;
            BigInteger da = (d * a) % P;
            BigInteger cb = (c * b) % P;
            BigInteger sum = (da + cb) % P;
            BigInteger dif = (da - cb) % P;
            x3 = (sum * sum) % P;
            z3 = (x1 * ((dif * dif) % P)) % P;
            x2 = (aa * bb) % P;
            z2 = (e * ((aa + (A24 * e) % P) % P)) % P;
        }
        CSwap(swap, ref x2, ref x3);
        CSwap(swap, ref z2, ref z3);
        BigInteger inv = BigInteger.ModPow(z2, P - 2, P);
        return To32((x2 * inv) % P);
    }

    public static byte[] PublicKey(byte[] priv)
    {
        var basePoint = new byte[32];
        basePoint[0] = 9;
        return ScalarMult(priv, basePoint);
    }

    public static byte[] RandomBytes(int count)
    {
        var buffer = new byte[count];
        var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);
        return buffer;
    }
}
'@
    try {
        Add-Type -TypeDefinition $source -Language CSharp -ReferencedAssemblies 'System.Numerics' -ErrorAction Stop
        return $true
    } catch {
        Write-WarpLog "X25519 derleyicisi yuklenemedi: $($_.Exception.Message)" 'ERROR'
        Write-WarpLog 'Bu makinede .NET Framework 4.x C# derleyicisi (csc.exe) yok veya engelli.' 'WARN'
        return $false
    }
}

function New-WarpKeyPair {
    $private = [WarpCordCrypto]::RandomBytes(32)
    $public = [WarpCordCrypto]::PublicKey($private)
    return @{
        PrivateKey = [Convert]::ToBase64String($private)
        PublicKey  = [Convert]::ToBase64String($public)
    }
}

function ConvertTo-WarpAccount {
    param($Response, [string]$PrivateKey)
    if (-not $Response -or -not $Response.config -or -not $Response.config.peers) { return $null }
    $config = $Response.config
    $peer = $config.peers[0]
    $clientId = [string]$config.client_id
    $reserved = @(0, 0, 0)
    if ($clientId) {
        try {
            $decoded = [Convert]::FromBase64String($clientId)
            if ($decoded.Length -ge 3) { $reserved = @([int]$decoded[0], [int]$decoded[1], [int]$decoded[2]) }
        } catch { }
    }
    $ports = @($script:WarpEndpointPorts)
    if ($peer.endpoint -and $peer.endpoint.ports) { $ports = @($peer.endpoint.ports | ForEach-Object { [int]$_ }) }
    $endpointV4 = ''
    $endpointV6 = ''
    if ($peer.endpoint) {
        if ($peer.endpoint.v4) { $endpointV4 = ([string]$peer.endpoint.v4 -replace ':[0-9]+$', '') }
        if ($peer.endpoint.v6) {
            $endpointV6 = [string]$peer.endpoint.v6
            if ($endpointV6 -match '^\[([^\]]+)\]') { $endpointV6 = $matches[1] }
            else { $endpointV6 = ($endpointV6 -replace ':[0-9]+$', '') }
        }
    }
    return @{
        PrivateKey     = $PrivateKey
        PublicKey      = [string]$Response.key
        AddressV4      = [string]$config.interface.addresses.v4
        AddressV6      = [string]$config.interface.addresses.v6
        PeerPublicKey  = [string]$peer.public_key
        Reserved       = $reserved
        ClientId       = $clientId
        DeviceId       = [string]$Response.id
        AccountId      = [string]$Response.account.id
        EndpointV4     = $endpointV4
        EndpointV6     = $endpointV6
        EndpointHost   = [string]$peer.endpoint.host
        Ports          = $ports
        RegisteredAt   = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    }
}

# ----------------------------------------------------------- registration --
function Invoke-WarpPostJson {
    # Bounded by a job: Invoke-RestMethod has no reliable hard timeout in
    # PowerShell 5.1, and an installer must never hang on a silent server.
    param([string]$Uri, [string]$Json, [hashtable]$Headers, [string]$Proxy = '', [int]$TimeoutSec = 45, [switch]$Insecure)
    $job = Start-Job -ScriptBlock {
        param($uri, $body, $hdrs, $proxy, $timeout, $insecure)
        try {
            if ($insecure) { [Net.ServicePointManager]::ServerCertificateValidationCallback = { param($a, $b, $c, $d) $true } }
            $params = @{
                Uri             = $uri
                Method          = 'Post'
                Body            = $body
                ContentType     = 'application/json'
                Headers         = $hdrs
                TimeoutSec      = $timeout
                UseBasicParsing = $true
            }
            if ($proxy) { $params['Proxy'] = $proxy }
            $response = Invoke-RestMethod @params
            return @{ Ok = $true; Json = ($response | ConvertTo-Json -Depth 25); Error = '' }
        } catch {
            return @{ Ok = $false; Json = ''; Error = $_.Exception.Message }
        }
    } -ArgumentList $Uri, $Json, $Headers, $Proxy, $TimeoutSec, [bool]$Insecure
    $output = $null
    try {
        if (Wait-Job -Job $job -Timeout ($TimeoutSec + 10)) {
            $output = Receive-Job -Job $job -ErrorAction SilentlyContinue | Select-Object -Last 1
        }
    } finally {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    if (-not $output) { return @{ Ok = $false; Json = ''; Error = 'istek zaman asimi' } }
    return $output
}

function New-WarpRegistration {
    param(
        [string]$Proxy = '',
        [int]$TimeoutSec = 45,
        [switch]$AllowInsecure
    )

    if (-not (Initialize-WarpCrypto)) { return $null }

    $keyPair = New-WarpKeyPair
    $body = [ordered]@{
        key           = $keyPair.PublicKey
        install_id    = ''
        fcm_token     = ''
        tos           = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
        model         = 'PC'
        serial_number = ''
        locale        = 'en_US'
    }
    $json = $body | ConvertTo-Json -Compress
    $headers = @{
        'User-Agent'        = 'okhttp/3.12.1'
        'CF-Client-Version' = 'a-6.30-3106'
        'Accept'            = 'application/json'
    }

    $versions = @('v0a5641', 'v0a2158', 'v0i1909')
    $attempts = @()
    foreach ($version in $versions) {
        $uri = "https://api.cloudflareclient.com/$version/reg"
        $attempts += @{ Uri = $uri; Proxy = $Proxy }
        if ($Proxy) { $attempts += @{ Uri = $uri; Proxy = '' } }
    }

    foreach ($attempt in $attempts) {
        $label = $attempt.Uri
        if ($attempt.Proxy) { $label = "$label (proxy $($attempt.Proxy))" }
        $post = Invoke-WarpPostJson -Uri $attempt.Uri -Json $json -Headers $headers `
            -Proxy $attempt.Proxy -TimeoutSec $TimeoutSec -Insecure:$AllowInsecure
        if (-not $post.Ok) {
            Write-WarpLog "kayit denemesi basarisiz: $label -> $($post.Error)" 'WARN'
            continue
        }
        try {
            $response = $post.Json | ConvertFrom-Json
            if (-not $response -or -not $response.config) { throw 'cevap beklenen formatta degil' }
            $account = ConvertTo-WarpAccount -Response $response -PrivateKey $keyPair.PrivateKey
            if (-not $account -or -not $account.PeerPublicKey) { throw 'peer bilgisi eksik' }
            Write-WarpLog "WARP cihazi kaydedildi ($($account.AddressV4))" 'OK'
            return $account
        } catch {
            Write-WarpLog "kayit cevabi islenemedi: $label -> $($_.Exception.Message)" 'WARN'
        }
    }
    return $null
}

function Get-WarpAccountFromConfig {
    param([string]$ConfigPath)
    $config = Read-WarpJsonFile $ConfigPath
    if (-not $config) { return $null }
    if (-not $config.endpoints) { return $null }
    $endpoint = $null
    foreach ($candidate in $config.endpoints) {
        if ($candidate.type -eq 'wireguard') { $endpoint = $candidate; break }
    }
    if (-not $endpoint -or -not $endpoint.peers -or $endpoint.peers.Count -lt 1) { return $null }
    $peer = $endpoint.peers[0]
    $address = ''
    if ($endpoint.address -and $endpoint.address.Count -gt 0) { $address = ([string]$endpoint.address[0] -replace '/[0-9]+$', '') }
    $reserved = @(0, 0, 0)
    if ($peer.reserved) { $reserved = @($peer.reserved | ForEach-Object { [int]$_ }) }
    return @{
        PrivateKey    = [string]$endpoint.private_key
        AddressV4     = $address
        AddressV6     = ''
        PeerPublicKey = [string]$peer.public_key
        Reserved      = $reserved
        ClientId      = ''
        DeviceId      = ''
        AccountId     = ''
        EndpointV4    = [string]$peer.address
        EndpointV6    = ''
        EndpointHost  = ''
        Ports         = @([int]$peer.port)
        RegisteredAt  = 'mevcut config'
        Reused        = $true
    }
}

# -------------------------------------------------------------- endpoints --
function Test-WarpHasIpv6 {
    try {
        $routes = Get-NetRoute -DestinationPrefix '::/0' -ErrorAction SilentlyContinue
        if (-not $routes) { return $false }
        $addresses = Get-NetIPAddress -AddressFamily IPv6 -ErrorAction SilentlyContinue |
            Where-Object { $_.PrefixOrigin -ne 'WellKnown' -and $_.IPAddress -notmatch '^(fe80|::1)' }
        return [bool]($addresses)
    } catch {
        return $false
    }
}

function Test-WarpIpv6Disabled {
    # DisabledComponents=255 (or a non-zero mask) means IPv6 was turned off by a
    # tweak tool. sing-box's WireGuard endpoint then cannot bind its IPv6 socket
    # to the interface, and WARP mode cannot work on that machine.
    try {
        $value = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters' `
            -Name 'DisabledComponents' -ErrorAction SilentlyContinue).DisabledComponents
        if ($null -ne $value -and [uint32]$value -ne 0) { return $true }
    } catch { }
    try {
        $adapter = Get-WarpPhysicalAdapter
        if ($adapter) {
            $binding = Get-NetAdapterBinding -Name $adapter.Alias -ComponentID 'ms_tcpip6' -ErrorAction SilentlyContinue
            if ($binding -and -not $binding.Enabled) { return $true }
        }
    } catch { }
    return $false
}

function Get-WarpEndpointCandidates {
    param($Account, [int]$MaxCount = 10)
    $list = New-Object System.Collections.ArrayList
    $seen = @{}
    $add = {
        param([string]$address, [int]$port, [string]$label)
        if (-not $address -or $port -le 0) { return }
        if ($list.Count -ge $MaxCount) { return }
        $key = "$address|$port"
        if ($seen.ContainsKey($key)) { return }
        $seen[$key] = $true
        [void]$list.Add([pscustomobject]@{ Address = $address; Port = $port; Label = $label })
    }

    $primary = '162.159.192.1'
    if ($Account -and $Account.EndpointV4) { $primary = $Account.EndpointV4 }
    $ports = @($script:WarpEndpointPorts)
    if ($Account -and $Account.Ports -and $Account.Ports.Count -gt 0) {
        $ports = @($Account.Ports) + ($script:WarpEndpointPorts | Where-Object { $Account.Ports -notcontains $_ })
    }

    # 1) The address Cloudflare handed us, all of its supported ports: port
    #    2408 is the one Turkish ISPs like to filter, 500/4500 often survive.
    foreach ($port in ($ports | Select-Object -Unique)) { & $add $primary ([int]$port) 'v4' }

    # 2) Same ports on the well-known anycast addresses.
    foreach ($ip in $script:WarpEndpointIps) {
        foreach ($port in @(2408, 500)) { & $add $ip ([int]$port) 'v4' }
    }

    # 3) IPv6 when the line actually has it (Superonline fiber often does, and
    #    IPv6 DPI is a lot weaker than the IPv4 kind).
    if (Test-WarpHasIpv6) {
        $v6 = @()
        if ($Account -and $Account.EndpointV6) { $v6 += $Account.EndpointV6 }
        $v6 += $script:WarpEndpointV6
        foreach ($ip in ($v6 | Select-Object -Unique)) {
            foreach ($port in @(2408, 500)) { & $add $ip ([int]$port) 'v6' }
        }
    }
    return $list.ToArray()
}

function New-WarpProbeConfig {
    param($Account, $Endpoint, [int]$ListenPort, [string]$LogPath)
    $rules = @(
        [ordered]@{ action = 'sniff' },
        [ordered]@{ protocol = 'dns'; action = 'hijack-dns' },
        [ordered]@{ domain_suffix = $script:DiscordSuffixes; outbound = 'warp' }
    )
    return [ordered]@{
        log = [ordered]@{ level = 'warn'; timestamp = $true; output = $LogPath }
        dns = [ordered]@{
            servers = @([ordered]@{ type = 'https'; tag = 'doh'; server = '1.1.1.1'; path = '/dns-query' })
            final   = 'doh'
            strategy = 'prefer_ipv4'
        }
        inbounds = @([ordered]@{ type = 'mixed'; tag = 'probe-in'; listen = '127.0.0.1'; listen_port = $ListenPort })
        outbounds = @([ordered]@{ type = 'direct'; tag = 'direct' })
        endpoints = @(New-WarpWireGuardEndpoint $Account $Endpoint)
        route = [ordered]@{
            default_domain_resolver = 'doh'
            # Important: mirrors the real tunnel config exactly. sing-box binds
            # outbound sockets to the detected interface when this is on, and on
            # Windows that is also the thing that fails when IPv6 is disabled.
            auto_detect_interface   = $true
            rules = $rules
            final = 'direct'
        }
    }
}

function New-WarpWireGuardEndpoint {
    param($Account, $Endpoint)
    # A lot of Windows machines have IPv6 disabled (DisabledComponents=255 or
    # ms_tcpip6 unbound). If we advertise ::/0 there, sing-box opens an AF_INET6
    # socket and the handshake dies with "address family not supported". So the
    # endpoint only claims IPv6 when the line can actually carry it.
    $hasV6 = $false
    try { $hasV6 = Test-WarpHasIpv6 } catch { $hasV6 = $false }

    $address = @()
    if ($Account.AddressV4) { $address += "$($Account.AddressV4)/32" }
    $allowedIps = @('0.0.0.0/0')
    if ($hasV6) {
        if ($Account.AddressV6) { $address += "$($Account.AddressV6)/128" }
        $allowedIps += '::/0'
    }
    $peer = [ordered]@{
        address                    = $Endpoint.Address
        port                       = [int]$Endpoint.Port
        public_key                 = $Account.PeerPublicKey
        reserved                   = @($Account.Reserved | ForEach-Object { [int]$_ })
        allowed_ips                = $allowedIps
        persistent_keepalive_interval = 25
    }
    return [ordered]@{
        type        = 'wireguard'
        tag         = 'warp'
        address     = $address
        private_key = $Account.PrivateKey
        mtu         = 1280
        peers       = @($peer)
    }
}

function Test-WarpEndpoint {
    param(
        [string]$SingBoxPath,
        $Account,
        $Endpoint,
        [string]$WorkDir,
        [int]$TimeoutSec = 6
    )
    $result = [pscustomobject]@{
        Address = $Endpoint.Address
        Port    = $Endpoint.Port
        Label   = $Endpoint.Label
        Ok      = $false
        Ms      = -1
        Detail  = ''
        Kind    = ''
    }
    $listenPort = Get-FreeTcpPort
    $probeConfig = Join-Path $WorkDir ("probe-{0}.json" -f ([guid]::NewGuid().ToString('N').Substring(0, 8)))
    $probeLog = Join-Path $WorkDir 'probe.log'
    Write-WarpJsonFile $probeConfig (New-WarpProbeConfig -Account $Account -Endpoint $Endpoint -ListenPort $listenPort -LogPath $probeLog)

    $process = $null
    try {
        $process = Start-Process -FilePath $SingBoxPath -ArgumentList @('run', '-c', "`"$probeConfig`"") -PassThru -WindowStyle Hidden
        $ready = $false
        for ($i = 0; $i -lt 50; $i++) {
            if (Test-TcpPort -Port $listenPort -TimeoutMs 200) { $ready = $true; break }
            if ($process.HasExited) { break }
            Start-Sleep -Milliseconds 120
        }
        if (-not $ready) {
            $result.Detail = 'sing-box baslamadi'
            return $result
        }
        $probe = Invoke-WarpTlsProbe -HostName 'discord.com' -Path '/api/v9/gateway' `
            -ProxyHost '127.0.0.1' -ProxyPort $listenPort -TimeoutSec $TimeoutSec
        $result.Ok = $probe.Ok
        $result.Ms = $probe.Ms
        $result.Detail = $probe.Detail
        $result.Kind = $probe.Kind
    } catch {
        $result.Detail = $_.Exception.Message
    } finally {
        if ($process -and -not $process.HasExited) {
            try { $process.Kill(); [void]$process.WaitForExit(3000) } catch { }
        }
        Remove-Item $probeConfig -Force -ErrorAction SilentlyContinue
    }
    return $result
}

function Select-WarpEndpoint {
    param(
        [string]$SingBoxPath,
        $Account,
        [string]$WorkDir,
        [int]$BudgetSeconds = 90,
        [string]$Override = ''
    )
    $candidates = @(Get-WarpEndpointCandidates -Account $Account)
    if ($Override) {
        $parts = $Override.Split(':')
        $candidates = @([pscustomobject]@{ Address = $parts[0]; Port = [int]$parts[1]; Label = 'manual' })
    }
    Write-WarpLog "WARP uclari deneniyor ($($candidates.Count) aday)..." 'STEP'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $best = $null
    $successes = 0
    foreach ($candidate in $candidates) {
        if ($watch.Elapsed.TotalSeconds -gt $BudgetSeconds) {
            Write-WarpLog 'Sure siniri asildi, kalan adaylar atlandi.' 'WARN'
            break
        }
        $probe = Test-WarpEndpoint -SingBoxPath $SingBoxPath -Account $Account -Endpoint $candidate -WorkDir $WorkDir
        if ($probe.Ok) {
            Write-WarpLog ("{0}:{1} -> OK ({2} ms)" -f $probe.Address, $probe.Port, $probe.Ms) 'OK'
            $successes++
            if (-not $best -or $probe.Ms -lt $best.Ms) { $best = $probe }
            # Good enough, do not keep the user waiting.
            if ($best.Ms -le 1200 -or $successes -ge 3) { break }
        } else {
            $reason = switch ($probe.Kind) {
                'timeout' { 'zaman asimi' }
                'tls'     { 'TLS reddedildi (reset/sertifika)' }
                'io'      { 'baglanti kesildi' }
                'socket'  { 'soket hatasi' }
                'proxy'   { 'proxy reddetti' }
                default   { 'yanit yok' }
            }
            Write-WarpLog ("{0}:{1} -> yok ({2})" -f $probe.Address, $probe.Port, $reason) 'WARN'
        }
    }
    return $best
}

# ---------------------------------------------------------------- configs --
function New-WarpTunnelConfig {
    param($Account, $Endpoint, [string]$LogPath)
    return [ordered]@{
        log = [ordered]@{ level = 'info'; timestamp = $true; output = $LogPath }
        dns = [ordered]@{
            servers = @([ordered]@{ type = 'https'; tag = 'doh'; server = '1.1.1.1'; path = '/dns-query' })
            final   = 'doh'
            strategy = 'prefer_ipv4'
        }
        inbounds = @([ordered]@{
            type           = 'tun'
            tag            = 'tun-in'
            interface_name = $script:WarpCordTunName
            address        = @('172.19.0.1/30')
            mtu            = 1400
            auto_route     = $true
            strict_route   = $false
            stack          = 'mixed'
        })
        outbounds = @([ordered]@{ type = 'direct'; tag = 'direct' })
        endpoints = @(New-WarpWireGuardEndpoint $Account $Endpoint)
        route = [ordered]@{
            default_domain_resolver = 'doh'
            auto_detect_interface   = $true
            rules = @(
                [ordered]@{ action = 'sniff' },
                [ordered]@{ protocol = 'dns'; action = 'hijack-dns' },
                [ordered]@{ domain_suffix = $script:DiscordSuffixes; outbound = 'warp' },
                [ordered]@{ process_name = $script:DiscordProcesses; outbound = 'warp' },
                [ordered]@{ process_path_regex = @($script:DiscordProcessRegex); outbound = 'warp' }
            )
            final = 'direct'
        }
    }
}

function New-WarpDpiConfig {
    param([string]$LogPath, [int]$FragPort)
    return [ordered]@{
        log = [ordered]@{ level = 'info'; timestamp = $true; output = $LogPath }
        dns = [ordered]@{
            # The DoH request itself goes through the fragmenting proxy, so even
            # a filtered "cloudflare-dns.com" name cannot break resolution.
            servers = @([ordered]@{ type = 'https'; tag = 'doh'; server = '1.1.1.1'; path = '/dns-query'; detour = 'frag' })
            final   = 'doh'
            strategy = 'prefer_ipv4'
        }
        inbounds = @([ordered]@{
            type           = 'tun'
            tag            = 'tun-in'
            interface_name = $script:WarpCordTunName
            address        = @('172.19.0.1/30')
            mtu            = 1400
            auto_route     = $true
            strict_route   = $false
            stack          = 'mixed'
        })
        outbounds = @(
            [ordered]@{ type = 'direct'; tag = 'direct' },
            [ordered]@{ type = 'http'; tag = 'frag'; server = '127.0.0.1'; server_port = [int]$FragPort }
        )
        route = [ordered]@{
            default_domain_resolver = 'doh'
            auto_detect_interface   = $true
            rules = @(
                [ordered]@{ action = 'sniff' },
                [ordered]@{ protocol = 'dns'; action = 'hijack-dns' },
                [ordered]@{ network = 'tcp'; domain_suffix = $script:DiscordSuffixes; outbound = 'frag' },
                [ordered]@{ network = 'tcp'; process_name = $script:DiscordProcesses; outbound = 'frag' },
                [ordered]@{ network = 'tcp'; process_path_regex = @($script:DiscordProcessRegex); outbound = 'frag' }
            )
            final = 'direct'
        }
    }
}

# ------------------------------------------------------------ frag helper --
function Get-WarpFragSource {
    param([string]$ScriptRoot)
    $path = Join-Path $ScriptRoot 'tools\warpfrag\WarpFrag.cs'
    if (Test-Path $path) { return $path }
    return $null
}

function Build-WarpFrag {
    param([string]$SourcePath, [string]$OutputPath)
    if (-not (Test-Path $SourcePath)) {
        Write-WarpLog "WarpFrag kaynagi bulunamadi: $SourcePath" 'ERROR'
        return $false
    }
    try {
        if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force -ErrorAction SilentlyContinue }
        $source = Get-Content -Path $SourcePath -Raw -ErrorAction Stop
        Add-Type -TypeDefinition $source -Language CSharp -OutputAssembly $OutputPath -OutputType ConsoleApplication -ErrorAction Stop
        return (Test-Path $OutputPath)
    } catch {
        Write-WarpLog "WarpFrag derlenemedi: $($_.Exception.Message)" 'ERROR'
        return $false
    }
}

function Test-WarpFrag {
    param([string]$FragPath, [int]$Port, [string]$LogPath, [int]$TimeoutSec = 12)
    $last = $null
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $last = Invoke-WarpFragTest -FragPath $FragPath -Port $Port -LogPath $LogPath -TimeoutSec $TimeoutSec
        if ($last.Ok) { return $last }
        if ($attempt -lt 2) {
            Write-WarpLog "DPI proxy testi tekrar deneniyor: $($last.Detail)" 'WARN'
            Start-Sleep -Milliseconds 400
        }
    }
    return $last
}

function Invoke-WarpFragTest {
    param([string]$FragPath, [int]$Port, [string]$LogPath, [int]$TimeoutSec = 12)
    $result = [pscustomobject]@{ Ok = $false; Ms = -1; Detail = '' }
    if (-not (Test-Path $FragPath)) { $result.Detail = 'warp-frag.exe yok'; return $result }
    $process = $null
    try {
        $process = Start-Process -FilePath $FragPath -ArgumentList @('--port', "$Port", '--mode', 'tlsrec', '--log', "`"$LogPath`"") -PassThru -WindowStyle Hidden
        $ready = $false
        for ($i = 0; $i -lt 30; $i++) {
            if (Test-TcpPort -Port $Port -TimeoutMs 200) { $ready = $true; break }
            if ($process.HasExited) { break }
            Start-Sleep -Milliseconds 100
        }
        if (-not $ready) { $result.Detail = 'proxy baslamadi'; return $result }
        $probe = Invoke-WarpTlsProbe -HostName 'discord.com' -Path '/api/v9/gateway' `
            -ProxyHost '127.0.0.1' -ProxyPort $Port -TimeoutSec $TimeoutSec
        $result.Ok = $probe.Ok
        $result.Ms = $probe.Ms
        $result.Detail = $probe.Detail
        $result.Kind = $probe.Kind
    } catch {
        $result.Detail = $_.Exception.Message
    } finally {
        if ($process -and -not $process.HasExited) {
            try { $process.Kill(); [void]$process.WaitForExit(3000) } catch { }
        }
    }
    return $result
}

# ------------------------------------------------------------- diagnostics --
function Test-DiscordDns {
    $result = [pscustomobject]@{ Ok = $false; Addresses = @(); Blocked = $false; Detail = '' }
    try {
        $answers = @(Resolve-DnsName -Name 'discord.com' -Type A -ErrorAction Stop |
            Where-Object { $_.IPAddress } | Select-Object -ExpandProperty IPAddress)
        $result.Addresses = $answers
        if ($answers.Count -eq 0) {
            $result.Detail = 'cevap yok'
        } elseif ($answers -contains '195.175.254.2') {
            $result.Blocked = $true
            $result.Detail = 'ISP blok sayfasi IPsi'
        } else {
            $result.Ok = $true
            $result.Detail = ($answers -join ', ')
        }
    } catch {
        $result.Detail = $_.Exception.Message
    }
    return $result
}

function Test-DiscordApi {
    param([int]$TimeoutSec = 12)
    $probe = Invoke-WarpTlsProbe -HostName 'discord.com' -Path '/api/v9/gateway' -TimeoutSec $TimeoutSec
    return [pscustomobject]@{ Ok = $probe.Ok; Ms = $probe.Ms; Detail = $probe.Detail }
}

function Get-PublicIp {
    param([int]$TimeoutSec = 8)
    $probe = Invoke-WarpTlsProbe -HostName 'api.ipify.org' -Path '/?format=json' -TimeoutSec $TimeoutSec -Expect '"ip"\s*:\s*"([0-9.]+)"'
    if (-not $probe.Ok) { return '' }
    if ($probe.Detail -match '"ip"\s*:\s*"([0-9.]+)"') { return $Matches[1] }
    return ''
}

function Get-WarpPhysicalAdapter {
    $routes = @(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Where-Object { $_.NextHop -ne '0.0.0.0' -and $_.InterfaceAlias -notmatch 'WarpCord' } |
        Sort-Object RouteMetric)
    if ($routes.Count -eq 0) { return $null }
    # Prefer a real physical adapter; fall back to whatever is carrying traffic
    # (PPPoE, unusual drivers, VPN users) instead of silently doing nothing.
    $preferred = @($routes | Where-Object { $_.InterfaceAlias -notmatch 'tun|sing-box|Tailscale|vEthernet|Loopback' })
    $route = if ($preferred.Count -gt 0) { $preferred[0] } else { $routes[0] }
    $adapter = $null
    if ($route.InterfaceAlias) {
        $adapter = Get-NetAdapter -Name $route.InterfaceAlias -ErrorAction SilentlyContinue
    }
    if (-not $adapter -and $route.InterfaceIndex) {
        # Some connections (PPPoE, older drivers) expose no matching alias.
        $adapter = Get-NetAdapter -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue
    }
    return [pscustomobject]@{
        Alias       = if ($adapter) { [string]$adapter.Name } else { [string]$route.InterfaceAlias }
        Guid        = if ($adapter) { [string]$adapter.InterfaceGuid } else { '' }
        Index       = [int]$route.InterfaceIndex
        Status      = if ($adapter) { [string]$adapter.Status } else { '' }
        Metric      = $route.RouteMetric
        Description = if ($adapter) { [string]$adapter.InterfaceDescription } else { '' }
    }
}

function Get-WarpRegistryDns {
    param([string]$Guid, [ValidateSet('v4', 'v6')][string]$Family = 'v4')
    if (-not $Guid) { return '' }
    $key = if ($Family -eq 'v6') {
        "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\$Guid"
    } else {
        "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$Guid"
    }
    try {
        $value = (Get-ItemProperty -Path $key -Name 'NameServer' -ErrorAction SilentlyContinue).NameServer
        return [string]$value
    } catch {
        return ''
    }
}

function Clear-WarpDnsCache {
    # ipconfig instead of Clear-DnsClientCache: the CIM cmdlet can block for
    # tens of seconds on a busy WMI service, and this runs during install.
    & ipconfig /flushdns 2>&1 | Out-Null
}

function Set-WarpDnsAddresses {
    param([string]$Alias, [ValidateSet('ipv4', 'ipv6')][string]$Family, [string[]]$Servers)
    if (-not $Servers -or $Servers.Count -eq 0) { return $false }
    & netsh interface $Family set dnsservers "name=$Alias" static $Servers[0] primary 2>&1 | Out-Null
    $ok = ($LASTEXITCODE -eq 0)
    for ($i = 1; $i -lt $Servers.Count; $i++) {
        & netsh interface $Family add dnsservers "name=$Alias" $Servers[$i] "index=$($i + 1)" 2>&1 | Out-Null
    }
    return $ok
}

function Reset-WarpDnsAddresses {
    param([string]$Alias)
    & netsh interface ipv4 set dnsservers "name=$Alias" source=dhcp 2>&1 | Out-Null
    $v4 = $LASTEXITCODE
    & netsh interface ipv6 set dnsservers "name=$Alias" source=dhcp 2>&1 | Out-Null
    $v6 = $LASTEXITCODE
    return (($v4 -eq 0) -or ($v6 -eq 0))
}
function Get-WarpDnsBackupEntries {
    param([string]$InstallDir)
    $path = Join-Path $InstallDir 'dns-backup.json'
    $entries = New-Object System.Collections.ArrayList
    if (-not (Test-Path $path)) { return ,$entries }
    $raw = Read-WarpJsonFile $path
    if (-not $raw) { return ,$entries }
    if ($raw.entries) {
        foreach ($entry in $raw.entries) { [void]$entries.Add($entry) }
    } elseif ($raw.interface) {
        # Older installs stored a single adapter; keep it readable.
        [void]$entries.Add($raw)
    }
    return ,$entries
}

function Backup-WarpDns {
    # One entry per physical adapter: laptops switch between Wi-Fi and Ethernet,
    # and a re-run must back up the adapter that is carrying traffic now rather
    # than reusing the one from the previous network.
    param([string]$InstallDir)
    $path = Join-Path $InstallDir 'dns-backup.json'
    $entries = Get-WarpDnsBackupEntries -InstallDir $InstallDir
    $adapter = Get-WarpPhysicalAdapter
    if (-not $adapter) { return $null }
    foreach ($entry in $entries) {
        if (($entry.guid -and $entry.guid -eq $adapter.Guid) -or
            (-not $entry.guid -and $entry.interface -eq $adapter.Alias)) {
            return $entry
        }
    }
    $newEntry = [ordered]@{
        interface = $adapter.Alias
        index     = $adapter.Index
        guid      = $adapter.Guid
        staticV4  = (Get-WarpRegistryDns -Guid $adapter.Guid -Family 'v4')
        staticV6  = (Get-WarpRegistryDns -Guid $adapter.Guid -Family 'v6')
        createdAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    }
    [void]$entries.Add($newEntry)
    Write-WarpJsonFile $path ([ordered]@{ entries = @($entries) })
    return $newEntry
}

function Set-WarpDns {
    param($Backup, [switch]$IncludeV6)
    if (-not $Backup) { return $false }
    $alias = [string]$Backup.interface
    $index = 0
    if ($Backup.index) { $index = [int]$Backup.index }
    $v4 = @('1.1.1.1', '1.0.0.1')
    $v6 = @('2606:4700:4700::1111', '2606:4700:4700::1001')
    $ok = $false
    if ($alias) { $ok = Set-WarpDnsAddresses -Alias $alias -Family 'ipv4' -Servers $v4 }
    if (-not $ok -and $index -gt 0) {
        try { Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses $v4 -ErrorAction Stop; $ok = $true } catch { }
    }
    if (-not $ok -and $alias) {
        try { Set-DnsClientServerAddress -InterfaceAlias $alias -ServerAddresses $v4 -ErrorAction Stop; $ok = $true } catch { }
    }
    if ($ok) {
        # IPv6 resolvers would otherwise still hand out poisoned A records on
        # dual-stack lines. Failing here is harmless when there is no IPv6.
        if ($alias) { [void](Set-WarpDnsAddresses -Alias $alias -Family 'ipv6' -Servers $v6) }
        elseif ($index -gt 0) {
            try { Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses $v6 -ErrorAction SilentlyContinue } catch { }
        }
        Clear-WarpDnsCache
    }
    return $ok
}

function Restore-WarpDns {
    param([string]$InstallDir)
    $path = Join-Path $InstallDir 'dns-backup.json'
    if (-not (Test-Path $path)) { return $false }
    $entries = Get-WarpDnsBackupEntries -InstallDir $InstallDir
    $restored = 0
    foreach ($backup in $entries) {
        $adapter = $null
        if ($backup.guid) {
            $adapter = Get-NetAdapter -ErrorAction SilentlyContinue |
                Where-Object { $_.InterfaceGuid -eq [string]$backup.guid } | Select-Object -First 1
        }
        if (-not $adapter -and $backup.interface) {
            $adapter = Get-NetAdapter -Name ([string]$backup.interface) -ErrorAction SilentlyContinue
        }
        $alias = ''
        $index = 0
        if ($adapter) { $alias = $adapter.Name; $index = [int]$adapter.ifIndex }
        elseif ($backup.index) { $index = [int]$backup.index }
        if (-not $alias -and $index -le 0) {
            Write-WarpLog "DNS yedegi: '$($backup.interface)' adaptoru artik yok, atlandi." 'INFO'
            continue
        }
        $resetOk = $false
        if ($alias) { $resetOk = Reset-WarpDnsAddresses -Alias $alias }
        if (-not $resetOk -and $index -gt 0) {
            try { Set-DnsClientServerAddress -InterfaceIndex $index -ResetServerAddresses -ErrorAction Stop; $resetOk = $true } catch { }
        }
        $v4 = @()
        if ($backup.staticV4) { $v4 = @(([string]$backup.staticV4) -split '[, ]+' | Where-Object { $_ }) }
        if ($v4.Count -gt 0) {
            if ($alias) { [void](Set-WarpDnsAddresses -Alias $alias -Family 'ipv4' -Servers $v4) }
            elseif ($index -gt 0) {
                try { Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses $v4 -ErrorAction SilentlyContinue } catch { }
            }
        }
        $v6 = @()
        if ($backup.staticV6) { $v6 = @(([string]$backup.staticV6) -split '[, ]+' | Where-Object { $_ }) }
        if ($v6.Count -gt 0) {
            if ($alias) { [void](Set-WarpDnsAddresses -Alias $alias -Family 'ipv6' -Servers $v6) }
            elseif ($index -gt 0) {
                try { Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses $v6 -ErrorAction SilentlyContinue } catch { }
            }
        }
        $restored++
    }
    Clear-WarpDnsCache
    Remove-Item $path -Force -ErrorAction SilentlyContinue
    return ($restored -gt 0)
}
