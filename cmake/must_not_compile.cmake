# cmake -DCONFIG=<name>.cmake -P must_not_compile.cmake
#
# One must-not-compile check (see ss_must_not_compile in
# cmake/SourceSharpTests.cmake). CONFIG sets NAME, CXX, RSP (the compiler's
# arguments, one per line), ERR (where the diagnostics are kept) and EXPECT (a
# regular expression one `error:` line must match).
#
# Three outcomes, and only one is a pass:
#   the file COMPILED                      -> FAIL: the check it stands for
#                                             cannot fail, so it checks nothing
#   it failed, but not at EXPECT           -> FAIL: the failure is not evidence
#                                             for what it is being read as
#   it failed with an error line at EXPECT -> pass, and that line is printed

include("${CONFIG}")
foreach(v NAME CXX RSP ERR EXPECT)
  if(NOT DEFINED ${v} OR "${${v}}" STREQUAL "")
    message(FATAL_ERROR "must_not_compile: ${CONFIG} does not set ${v}")
  endif()
endforeach()

get_filename_component(errdir "${ERR}" DIRECTORY)
file(MAKE_DIRECTORY "${errdir}")
file(REMOVE "${ERR}")

execute_process(COMMAND "${CXX}" "@${RSP}"
  RESULT_VARIABLE rc OUTPUT_VARIABLE out ERROR_VARIABLE err)
file(WRITE "${ERR}" "${out}${err}")

if(NOT rc MATCHES "^[0-9]+$")
  message(FATAL_ERROR "!! ${NAME}: the compiler did not run to an exit status (${rc}), "
    "so nothing was checked.")
endif()
if(rc EQUAL 0)
  message(FATAL_ERROR "!! ${NAME} COMPILED. The check it stands for cannot fail, which\n"
    "!! means it is not checking anything.")
endif()

string(REPLACE ";" "\\;" text "${out}${err}")
string(REPLACE "\n" ";" lines "${text}")
set(hit "")
foreach(line IN LISTS lines)
  if(line MATCHES " error: " AND line MATCHES "${EXPECT}")
    set(hit "${line}")
    break()
  endif()
endforeach()

if(hit STREQUAL "")
  set(head "")
  set(n 0)
  foreach(line IN LISTS lines)
    string(APPEND head "     ${line}\n")
    math(EXPR n "${n} + 1")
    if(n GREATER_EQUAL 20)
      break()
    endif()
  endforeach()
  message(FATAL_ERROR "!! ${NAME} failed to compile, but no error line matches\n"
    "!! '${EXPECT}' -- so the failure is not evidence for what it is being\n"
    "!! read as.\n${head}")
endif()

message("     ${hit}")
