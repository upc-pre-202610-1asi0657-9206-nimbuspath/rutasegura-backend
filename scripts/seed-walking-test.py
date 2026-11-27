"""Repeatable API-only fixture. Creates no route/trip; never stores JWTs or touches EC2 configuration."""
import argparse
import json
import os
from pathlib import Path
import urllib.error
import urllib.request
from datetime import datetime, timezone


class ApiFailure(RuntimeError):
    def __init__(self, status, path, payload):
        self.status = status
        self.payload = payload
        super().__init__(f'{status} {path}: {payload.get("code", payload.get("title", "API error"))}')


class Api:
    def __init__(self, origin, token=None):
        self.origin, self.token = origin.rstrip('/'), token

    def call(self, method, path, body=None):
        headers = {'Accept': 'application/json'}
        if self.token:
            headers['Authorization'] = f'Bearer {self.token}'
        if body is not None:
            headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(self.origin + '/api/v1' + path,
            data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            try:
                payload = json.loads(error.read())
            except (ValueError, UnicodeError):
                payload = {}
            raise ApiFailure(error.code, path, payload) from None

    def sign_in(self, email, password):
        account = self.call('POST', '/users/sign-in', {'email': email, 'password': password})
        return Api(self.origin, account['token']), {key: account[key] for key in
            ('id', 'firstName', 'lastName', 'email', 'roleTier', 'organizationId')}


def ensure_account(public, admin, kind, email, password, first, last):
    try:
        return public.sign_in(email, password)
    except ApiFailure as error:
        if error.status not in (401, 404, 422):
            raise
    admin.call('POST', '/users/' + kind, dict(firstName=first, lastName=last, email=email, password=password))
    return public.sign_in(email, password)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', required=True)
    parser.add_argument('--admin-email', default='admin@saferoute.pe')
    parser.add_argument('--output', default='artifacts/walking-test/accounts.json')
    args = parser.parse_args()
    admin_password = os.environ.get('RUTASEGURA_SEED_ADMIN_PASSWORD')
    if not admin_password:
        raise RuntimeError('Set RUTASEGURA_SEED_ADMIN_PASSWORD; do not pass passwords on the command line.')
    public = Api(args.base_url)
    admin, administrator = public.sign_in(args.admin_email, admin_password)
    if administrator['roleTier'] != 'ADMIN':
        raise RuntimeError('The seed requires an administrator.')
    password = 'RutaPrueba2026!'
    driver, driver_user = ensure_account(public, admin, 'drivers', 'conductor.caminata@rutasegura.test',
        password, 'Conductor', 'Caminata Prueba')
    parent, parent_user = ensure_account(public, admin, 'parents', 'familia.caminata@rutasegura.test',
        password, 'Familia', 'Caminata Prueba')
    profiles = admin.call('GET', '/roster/drivers')
    profile = next((p for p in profiles if p['userId'] == driver_user['id']), None)
    if profile is None:
        profile = admin.call('POST', '/roster/drivers', dict(userId=driver_user['id'],
            firstName='Conductor', lastName='Caminata Prueba', documentNumber='92700421',
            licenseNumber='Q92700421', licenseCategory='A-IIA', licenseExpiresOn='2030-12-31', phoneNumber='900000421'))
    if profile['status'] == 'PENDING_VERIFICATION':
        admin.call('POST', f'/roster/drivers/{profile["id"]}/verify')
    elif profile['status'] != 'VERIFIED':
        raise RuntimeError('Test driver is suspended; refusing to reactivate automatically.')
    vehicles = admin.call('GET', '/roster/vehicles')
    vehicle = next((v for v in vehicles if v['licensePlate'] == 'RSC-427'), None)
    if vehicle is None:
        vehicle = admin.call('POST', '/roster/vehicles', dict(licensePlate='RSC-427', seatingCapacity=8,
            brand='Toyota', model='Hiace Prueba Caminata'))
    parent.call('PUT', '/stakeholders/me/profile', dict(phoneNumber='900000422', documentNumber='92700422'))
    admin.call('POST', '/stakeholders/administration/guardians', dict(guardianUserId=parent_user['id'],
        phoneNumber='900000422', documentNumber='92700422'))
    existing = parent.call('GET', '/stakeholders/me/students')
    links = admin.call('GET', '/stakeholders/administration/guardian-links')
    students = []
    legacy_names = ('Alumno Uno', 'Alumna Dos', 'Alumno Tres', 'Alumna Cuatro')
    for index, name in enumerate(('Mateo', 'Valentina', 'Lucas', 'Sofia'), start=1):
        student = next((s for s in existing if
            (s['firstName'] == name and s['lastName'] == 'Ramirez Torres') or
            (s['firstName'] == legacy_names[index - 1] and s['lastName'] == 'Caminata Prueba')), None)
        if student is not None and (student['firstName'] != name or student['lastName'] != 'Ramirez Torres'):
            student = parent.call('PUT', f'/stakeholders/me/students/{student["id"]}',
                dict(firstName=name, lastName='Ramirez Torres', birthDate=student.get('birthDate'),
                    schoolGrade=student.get('schoolGrade'), schoolName=student.get('schoolName')))
        pickup = dict(address=f'Punto ficticio {index} - cambiar antes de crear la ruta',
            reference='Datos sinteticos; seleccionar un punto real de la caminata antes de inscribir.',
            latitude=-12.0464 - (index - 1) * 0.0015, longitude=-77.0428)
        if student is None:
            created = admin.call('POST', '/stakeholders/administration/students', dict(guardianUserId=parent_user['id'],
                student=dict(firstName=name, lastName='Ramirez Torres', birthDate=f'201{index+5}-03-10',
                    schoolGrade=f'{index} primaria', schoolName='Colegio Los Alamos', pickupLocation=pickup)))
            student = parent.call('GET', f'/stakeholders/me/students/{created["studentId"]}')
        link = next((g for g in links if g['studentId'] == student['id'] and g['guardianUserId'] == parent_user['id']), None)
        if link is None or link['status'] != 'VERIFIED':
            admin.call('POST', f'/stakeholders/administration/guardian-links/{parent_user["id"]}/{student["id"]}/verify',
                dict(reference='Prueba sintetica de caminata autorizada; sin datos de menores reales.'))
        students.append(dict(id=student['id'], fullName=student['fullName'], pickupLocation=student['pickupLocation']))
    # Exercise the actual deployed contracts. No trip or announcement is created.
    contexts = parent.call('GET', '/notifications/contexts')
    agenda = driver.call('GET', '/drivers/me/trips')
    if administrator['organizationId'] not in contexts['organizationIds']:
        raise RuntimeError('The parent context was not enabled by verified links.')
    confirmed = parent.call('GET', '/stakeholders/me/students')
    if not all(any(c['id'] == s['id'] for c in confirmed) for s in students):
        raise RuntimeError('Not all four students are visible to the parent.')
    manifest = dict(generatedAt=datetime.now(timezone.utc).isoformat(), baseUrl=args.base_url,
        organizationId=administrator['organizationId'], administrator=administrator,
        driver=dict(**driver_user, profileId=profile['id'], password=password),
        parent=dict(**parent_user, password=password), vehicle=vehicle, students=students,
        checks=dict(parentSeesFourStudents=True, notificationContext=True, driverAgendaReachable=True,
            driverTrips=len(agenda)), routeCreated=False,
        note='Fictional pickup points must be changed by the family to the walking route before enrollment.')
    destination = Path(args.output)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(dict(manifest=str(destination.resolve()), driver=driver_user['email'], parent=parent_user['email'],
        students=len(students), driverProfile=profile['id'], vehicle=vehicle['licensePlate'], checks=manifest['checks']), ensure_ascii=False))


if __name__ == '__main__':
    main()
