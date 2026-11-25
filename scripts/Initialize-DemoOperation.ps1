[CmdletBinding()]
param(
    [SecureString]$AdminPassword,
    [switch]$SkipRoutes
)

$ErrorActionPreference = 'Stop'
$api = 'http://localhost:8000/api/v1'
$demoOrganization = 'a0000000-0000-0000-0000-000000000001'
$headers = @{}

function Invoke-DemoApi {
    param([string]$Method, [string]$Path, $Body)

    $request = @{ Uri = "$api$Path"; Method = $Method; Headers = $headers; TimeoutSec = 20 }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Body -Depth 8 -Compress))
    }
    try {
        Invoke-RestMethod @request
    } catch {
        throw "$Method $Path falló. $($_.ErrorDetails.Message ?? $_.Exception.Message)"
    }
}

if ($null -eq $AdminPassword) { $AdminPassword = Read-Host 'Contraseña del admin demo' -AsSecureString }
$login = Invoke-DemoApi POST '/users/sign-in' @{
    email = 'admin@saferoute.pe'
    password = [Net.NetworkCredential]::new('', $AdminPassword).Password
}
if ($login.roleTier -ne 'ADMIN' -or $login.organizationId -ne $demoOrganization) {
    throw 'El script solo carga la organización demo instalada por el seeder local.'
}
$headers.Authorization = "Bearer $($login.token)"

$accounts = Invoke-DemoApi GET '/users'
$drivers = Invoke-DemoApi GET '/roster/drivers'
$driverFixtures = @(
    @{ Email = 'driver@saferoute.pe'; Document = '99000101'; License = 'Q99000101'; Phone = '900000101' },
    @{ Email = 'luis.torres@saferoute.pe'; Document = '99000102'; License = 'Q99000102'; Phone = '900000102' }
)
$guardianFixtures = @(
    @{ Email = 'parent@saferoute.pe'; Phone = '900000201'; Students = @('Lucía', 'Mateo'); Route = 0 },
    @{ Email = 'mariana.flores@gmail.com'; Phone = '900000202'; Students = @('Camila', 'Diego'); Route = 1 }
)

function Get-DemoAccount {
    param([string]$Email, [string]$Role)
    $matches = @($accounts | Where-Object { $_.email -eq $Email -and $_.roleTier -eq $Role })
    if ($matches.Count -ne 1) { throw "Falta la cuenta local $Email con rol $Role. Revisa el seed de IAM." }
    $matches[0]
}

# Resolver las cuentas antes de crear registros para detectar un seed de IAM incompleto.
foreach ($fixture in $driverFixtures) { $fixture.Account = Get-DemoAccount $fixture.Email 'DRIVER' }
foreach ($fixture in $guardianFixtures) { $fixture.Account = Get-DemoAccount $fixture.Email 'PARENT' }

foreach ($fixture in $driverFixtures) {
    $account = $fixture.Account
    $driver = $drivers | Where-Object userId -eq $account.id | Select-Object -First 1
    if ($null -eq $driver) {
        $driver = Invoke-DemoApi POST '/roster/drivers' @{
            userId = $account.id; firstName = $account.firstName; lastName = $account.lastName
            documentNumber = $fixture.Document; licenseNumber = $fixture.License; licenseCategory = 'A-IIb'
            licenseExpiresOn = (Get-Date).AddYears(4).ToString('yyyy-MM-dd'); phoneNumber = $fixture.Phone
        }
    }
    if ($driver.status -eq 'PENDING_VERIFICATION' -and $driver.documentNumber -eq $fixture.Document -and
        $driver.licenseNumber -eq $fixture.License) {
        $null = Invoke-DemoApi POST "/roster/drivers/$($driver.id)/verify" @{}
    }
}

$vehicles = Invoke-DemoApi GET '/roster/vehicles'
foreach ($vehicle in @(
    @{ licensePlate = 'DEM-901'; seatingCapacity = 15; brand = 'Toyota'; model = 'Hiace' },
    @{ licensePlate = 'DEM-902'; seatingCapacity = 19; brand = 'Hyundai'; model = 'County' }
)) {
    if ($vehicles.licensePlate -notcontains $vehicle.licensePlate) {
        $null = Invoke-DemoApi POST '/roster/vehicles' $vehicle
    }
}

$routeFixtures = @(
    @{
        Name = '[DEMO] Recojo - Colegio Los Sauces'; School = 'Colegio Demo Los Sauces'; Direction = 'PICKUP'
        Departure = '06:45:00'; Arrival = '07:30:00'
        Pickup = @{ name = 'Parque de la Amistad'; address = 'Parque de la Amistad, Santiago de Surco, Lima'; latitude = -12.1283; longitude = -76.9829 }
        SchoolStop = @{ name = 'Colegio Demo Los Sauces'; address = 'Punto de referencia demo, Santiago de Surco, Lima'; latitude = -12.1185; longitude = -76.9904 }
    },
    @{
        Name = '[DEMO] Retorno - Colegio Amanecer'; School = 'Colegio Demo Amanecer'; Direction = 'RETURN'
        Departure = '14:45:00'; Arrival = '15:30:00'
        Pickup = @{ name = 'Parque Reducto N.° 2'; address = 'Parque Reducto N.° 2, Miraflores, Lima'; latitude = -12.1262; longitude = -77.0234 }
        SchoolStop = @{ name = 'Colegio Demo Amanecer'; address = 'Punto de referencia demo, Miraflores, Lima'; latitude = -12.1174; longitude = -77.0293 }
    }
)

$stakeholder = '/stakeholders/administration'
$students = Invoke-DemoApi GET "$stakeholder/students"
$links = Invoke-DemoApi GET "$stakeholder/guardian-links"
foreach ($fixture in $guardianFixtures) {
    $guardianId = $fixture.Account.id
    $null = Invoke-DemoApi POST "$stakeholder/guardians" @{ guardianUserId = $guardianId; phoneNumber = $fixture.Phone }
    $route = $routeFixtures[$fixture.Route]
    $fixture.StudentIds = @()
    foreach ($firstName in $fixture.Students) {
        $student = $students | Where-Object { $_.guardianUserId -eq $guardianId -and $_.fullName -eq "$firstName Demo" } | Select-Object -First 1
        if ($null -eq $student) {
            $student = Invoke-DemoApi POST "$stakeholder/students" @{
                guardianUserId = $guardianId
                student = @{
                    firstName = $firstName; lastName = 'Demo'; schoolGrade = '4.° de primaria'; schoolName = $route.School
                    pickupLocation = @{
                        address = $route.Pickup.address; reference = 'Punto público de demostración'
                        latitude = $route.Pickup.latitude; longitude = $route.Pickup.longitude
                    }
                }
            }
            $needsVerification = $true
        } else {
            $link = $links | Where-Object { $_.guardianUserId -eq $guardianId -and $_.studentId -eq $student.studentId } | Select-Object -First 1
            $needsVerification = $link.status -eq 'PENDING' -and $student.schoolName -eq $route.School
        }
        $fixture.StudentIds += $student.studentId
        if ($needsVerification) {
            $null = Invoke-DemoApi POST "$stakeholder/guardian-links/$guardianId/$($student.studentId)/verify" @{
                reference = 'Vínculo ficticio del entorno de demostración local'
            }
        }
    }
}

if (-not $SkipRoutes) {
    $routes = Invoke-DemoApi GET '/routes'
    foreach ($fixture in $guardianFixtures) {
        $demo = $routeFixtures[$fixture.Route]
        $route = $routes | Where-Object name -eq $demo.Name | Select-Object -First 1
        if ($null -eq $route) {
            $route = Invoke-DemoApi POST '/routes' @{
                name = $demo.Name; direction = $demo.Direction; departureTime = $demo.Departure
                estimatedArrivalTime = $demo.Arrival; timeZoneId = 'America/Lima'
                operatingDays = @('Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday')
            }
        }
        if ($route.status -ne 'DRAFT') { continue }
        $routePath = "/routes/$($route.id)"
        $stops = if ($demo.Direction -eq 'PICKUP') { @($demo.Pickup, $demo.SchoolStop) } else { @($demo.SchoolStop, $demo.Pickup) }
        foreach ($stop in $stops) {
            if ($route.stops.name -notcontains $stop.name) { $route = Invoke-DemoApi POST "$routePath/stops" $stop }
        }
        $pickupStop = $route.stops | Where-Object name -eq $demo.Pickup.name | Select-Object -First 1
        foreach ($studentId in $fixture.StudentIds) {
            $assignedStudentIds = @($route.stops | ForEach-Object { $_.studentIds })
            if ($assignedStudentIds -notcontains $studentId) {
                $route = Invoke-DemoApi PUT "$routePath/stops/$($pickupStop.id)/students/$studentId" @{}
            }
        }
    }
}

Write-Host 'Datos demo disponibles en la organización de admin@saferoute.pe.'
if (-not $SkipRoutes) { Write-Host 'Las rutas demo quedan en borrador. Para operarlas, asígnales recursos y actívalas desde la web.' }
