using '../main.bicep'

param workloadName = 'gateway'
param environment = 'prod'
param location = 'koreacentral'
param apiImage = 'REPLACE_WITH_API_IMAGE'
param uiImage = 'REPLACE_WITH_UI_IMAGE'
param mqttImage = 'eclipse-mosquitto:2.0'
param postgresAdminUsername = 'gatewayadmin'
param postgresAdminPassword = 'REPLACE_WITH_SECURE_PASSWORD'
param allowedUiCidrs = []
param monthlyBudgetUsd = 300
param logRetentionDays = 90
param apiMinReplicas = 1
param uiMinReplicas = 1
param mqttMinReplicas = 1
param useKeyVaultReferences = true
param deployKeyVaultRbac = true
