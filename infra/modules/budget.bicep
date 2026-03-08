param budgetName string
param amount int
param contactEmails array

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: budgetName
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: '2026-01-01T00:00:00Z'
      endDate: '2030-12-31T00:00:00Z'
    }
    notifications: {
      threshold70: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 70
        contactEmails: contactEmails
      }
      threshold90: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 90
        contactEmails: contactEmails
      }
      threshold100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: contactEmails
      }
    }
  }
}
