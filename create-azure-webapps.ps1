# Azure Web App 생성 스크립트
# 실행 전에 az login을 완료했는지 확인하세요

$resourceGroup = "rg-gateway-dev-korea-01"
$location = "koreacentral"  # 또는 "koreasouth"
$appServicePlanName = "asp-gateway-dev-korea"
# Web App 이름은 전역적으로 고유해야 합니다. 필요시 변경하세요
$apiAppName = "gateway-api-wltn9"  # 고유한 이름으로 변경
$uiAppName = "gateway-ui-wltn9"    # 고유한 이름으로 변경

# 구독 설정 (필요시)
# az account set --subscription "your-subscription-id"

Write-Host "Creating App Service Plan..." -ForegroundColor Green
az appservice plan create `
  --name $appServicePlanName `
  --resource-group $resourceGroup `
  --location $location `
  --sku F1 `
  --is-linux

Write-Host "Creating API Web App..." -ForegroundColor Green
cmd /c "az webapp create --name $apiAppName --resource-group $resourceGroup --plan $appServicePlanName --runtime `"DOTNETCORE:8.0`""

Write-Host "Creating UI Web App..." -ForegroundColor Green
cmd /c "az webapp create --name $uiAppName --resource-group $resourceGroup --plan $appServicePlanName --runtime `"DOTNETCORE:8.0`""

Write-Host "Web Apps created successfully!" -ForegroundColor Green
Write-Host "API URL: https://$apiAppName.azurewebsites.net" -ForegroundColor Cyan
Write-Host "UI URL: https://$uiAppName.azurewebsites.net" -ForegroundColor Cyan

