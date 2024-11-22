#include <iostream>
#include <windows.h>      // For DWORD, BOOL, etc.
#include <sql.h>
#include <sqlext.h>
#include <stdint.h>       // For INT64 and UINT64 types

int main() {
    SQLHENV hEnv = NULL;
    SQLHDBC hDbc = NULL;
    SQLRETURN ret;

    // Allocate an environment handle
    ret = SQLAllocHandle(SQL_HANDLE_ENV, SQL_NULL_HANDLE, &hEnv);
    if (ret != SQL_SUCCESS) {
        std::cerr << "Error allocating environment handle" << std::endl;
        return -1;
    }

    // Set ODBC version
    ret = SQLSetEnvAttr(hEnv, SQL_ATTR_ODBC_VERSION, (SQLPOINTER)SQL_OV_ODBC3, 0);
    if (ret != SQL_SUCCESS) {
        std::cerr << "Error setting ODBC version" << std::endl;
        SQLFreeHandle(SQL_HANDLE_ENV, hEnv);
        return -1;
    }

    // Allocate a database connection handle
    ret = SQLAllocHandle(SQL_HANDLE_DBC, hEnv, &hDbc);
    if (ret != SQL_SUCCESS) {
        std::cerr << "Error allocating connection handle" << std::endl;
        SQLFreeHandle(SQL_HANDLE_ENV, hEnv);
        return -1;
    }

    // Connect to the database (use wide strings for Unicode)
    ret = SQLConnect(hDbc,
        (SQLWCHAR*)L"YourDataSourceName", SQL_NTS,
        (SQLWCHAR*)L"your_user", SQL_NTS,
        (SQLWCHAR*)L"your_password", SQL_NTS);
    if (ret != SQL_SUCCESS) {
        std::cerr << "Error connecting to the database" << std::endl;
        SQLFreeHandle(SQL_HANDLE_DBC, hDbc);
        SQLFreeHandle(SQL_HANDLE_ENV, hEnv);
        return -1;
    }

    std::cout << "Connected to the database successfully!" << std::endl;

    // Clean up
    SQLDisconnect(hDbc);
    SQLFreeHandle(SQL_HANDLE_DBC, hDbc);
    SQLFreeHandle(SQL_HANDLE_ENV, hEnv);

    return 0;
}
