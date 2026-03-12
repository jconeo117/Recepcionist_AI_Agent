USE ReceptionistAI_AgentCore;
GO

-- Delete existing Providers first (Foreign Key constraint equivalent)
DELETE FROM TenantProviders WHERE TenantId IN ('clinica-salud-total', 'estudio-belle', 'unas-maria-estrella', 'yoga-serena');
DELETE FROM TenantBilling WHERE TenantId IN ('clinica-salud-total', 'estudio-belle', 'unas-maria-estrella', 'yoga-serena');
DELETE FROM Tenants WHERE TenantId IN ('clinica-salud-total', 'estudio-belle', 'unas-maria-estrella', 'yoga-serena');
GO

-- 1. Clinica Salud Total
INSERT INTO Tenants (TenantId, BusinessName, BusinessType, DbType, ConnectionString, TimeZoneId, Address, Phone, WorkingHours, Services, AcceptedInsurance, Pricing, CustomSettings, Username, PasswordHash, IsActive, CreatedAt)
VALUES (
    'clinica-salud-total', 'Clínica Salud Total', 'clinic', 'SqlServer', 'Server=(localdb)\ReceptionistAI;Database=Client_ClinicaSaludTotal;Trusted_Connection=True;', 'America/Bogota', 'Calle Principal 123', '555-0101', 'Lun-Vie: 8am-8pm, Sab: 8am-2pm',
    '["Consulta General", "Odontología", "Pediatría"]', '["Sanitas", "Sura", "Coomeva"]', '{"Consulta General": "$50", "Odontología": "$80", "Pediatría": "$60"}', '{}',
    'admin', 'password123', 1, GETUTCDATE()
);
INSERT INTO TenantProviders (Id, TenantId, Name, Role, WorkingDays, StartTime, EndTime, SlotDurationMin, IsActive)
VALUES ('DR01', 'clinica-salud-total', 'Dr. Andrés Pérez', 'Médico General', '["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]', '08:00', '16:00', 30, 1);

-- 2. Estudio Belle
INSERT INTO Tenants (TenantId, BusinessName, BusinessType, DbType, ConnectionString, TimeZoneId, Address, Phone, WorkingHours, Services, AcceptedInsurance, Pricing, CustomSettings, Username, PasswordHash, IsActive, CreatedAt)
VALUES (
    'estudio-belle', 'Estudio Belle', 'salon', 'SqlServer', 'Server=(localdb)\ReceptionistAI;Database=Client_EstudioBelle;Trusted_Connection=True;', 'America/Bogota', 'Avenida Rosa 45', '555-0202', 'Mar-Dom: 10am-7pm',
    '["Corte de Pelo", "Tinte", "Peinado", "Maquillaje"]', '[]', '{"Corte de Pelo": "$30", "Tinte": "$70"}', '{}',
    'belle_admin', 'Belle!2026', 1, GETUTCDATE()
);
INSERT INTO TenantProviders (Id, TenantId, Name, Role, WorkingDays, StartTime, EndTime, SlotDurationMin, IsActive)
VALUES ('ST01', 'estudio-belle', 'Laura Gómez', 'Estilista Principal', '["Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"]', '10:00', '19:00', 45, 1);

-- 3. Uñas by María Estrella
INSERT INTO Tenants (TenantId, BusinessName, BusinessType, DbType, ConnectionString, TimeZoneId, Address, Phone, WorkingHours, Services, AcceptedInsurance, Pricing, CustomSettings, Username, PasswordHash, IsActive, CreatedAt)
VALUES (
    'unas-maria-estrella', 'Uñas by María Estrella', 'nails', 'SqlServer', 'Server=(localdb)\ReceptionistAI;Database=Client_UnasMaria;Trusted_Connection=True;', 'America/Bogota', 'C.C. El Sol, Local 12', '555-0303', 'Lun-Sab: 9am-6pm',
    '["Manicura Clásica", "Pedicura Spa", "Uñas Acrílicas"]', '[]', '{"Manicura Clásica": "$15", "Pedicura Spa": "$25"}', '{}',
    'maria_u_adm', 'Estrella#99', 1, GETUTCDATE()
);
INSERT INTO TenantProviders (Id, TenantId, Name, Role, WorkingDays, StartTime, EndTime, SlotDurationMin, IsActive)
VALUES ('MA01', 'unas-maria-estrella', 'María Estrella', 'Manicurista', '["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"]', '09:00', '18:00', 60, 1);

-- 4. Centro de Yoga Serena
INSERT INTO Tenants (TenantId, BusinessName, BusinessType, DbType, ConnectionString, TimeZoneId, Address, Phone, WorkingHours, Services, AcceptedInsurance, Pricing, CustomSettings, Username, PasswordHash, IsActive, CreatedAt)
VALUES (
    'yoga-serena', 'Centro de Yoga Serena', 'wellness', 'SqlServer', 'Server=(localdb)\ReceptionistAI;Database=Client_YogaSerena;Trusted_Connection=True;', 'America/Bogota', 'Parque Central, Edif. Zen', '555-0404', 'Lun-Dom: 6am-9pm',
    '["Hatha Yoga", "Vinyasa", "Meditación Guiada"]', '[]', '{"Hatha Yoga": "$10", "Vinyasa": "$12"}', '{}',
    'serena_yoga', 'Namaste@20', 1, GETUTCDATE()
);
INSERT INTO TenantProviders (Id, TenantId, Name, Role, WorkingDays, StartTime, EndTime, SlotDurationMin, IsActive)
VALUES ('YG01', 'yoga-serena', 'Sofia Paz', 'Instructora', '["Monday", "Wednesday", "Friday"]', '06:00', '12:00', 60, 1);
GO
