<script setup lang="ts" name="UiSpacer">
interface Props {
    size?: string | number
    axis?: 'x' | 'y'
}
const { size, axis } = withDefaults(defineProps<Props>(), {
    size: '1rem',
    axis: 'y'
})

const styleObject = computed<Record<string, string>>(() => {
    const value = typeof size === 'number' ? `${size}px` : size
    return axis === 'x'
        ? { display: 'inline-block', width: value, height: '1px' }
        : { display: 'block', width: '100%', height: value }
})
</script>

<template>
    <div :style="styleObject"
         aria-hidden="true" />
</template>
docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P %MSSQL_SA_PASSWORD% -Q "SELECT SERVERPROPERTY('IsFullTextInstalled') AS ServerProp, FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS FtService; SELECT value FROM sys.configurations WHERE name='transform noise words';"docker exec -it spocr-sample-sql /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P %MSSQL_SA_PASSWORD% -Q "SELECT SERVERPROPERTY('IsFullTextInstalled') AS ServerProp, FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') AS FtService; SELECT value FROM sys.configurations WHERE name='transform noise words';"
<style scoped>
/* Lightweight spacer; prefer CSS gap when using flex or grid containers. */
</style>
