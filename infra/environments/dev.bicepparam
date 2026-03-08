using '../main.bicep'

param workloadName = 'gateway'
param environment = 'dev'
param location = 'koreacentral'
param apiImage = 'REPLACE_WITH_API_IMAGE'
param uiImage = 'REPLACE_WITH_UI_IMAGE'
param mqttImage = 'eclipse-mosquitto:2.0'
param postgresAdminUsername = 'gatewayadmin'
param postgresAdminPassword = 'REPLACE_WITH_SECURE_PASSWORD'
param allowedUiCidrs = []
param monthlyBudgetUsd = 80
param logRetentionDays = 30
param apiMinReplicas = 0
param uiMinReplicas = 0
param mqttMinReplicas = 1
