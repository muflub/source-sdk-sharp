# cmake -DIN=<built .so> -DDEST_DIR=<dir> -DSPLIT=<0|1> -DOBJCOPY=<objcopy> -P deploy.cmake
#
# Puts a freshly linked library into the game directory by copy-then-rename, so
# the file a running game has mapped (and `make run`'s snapshot has hard-linked)
# is replaced rather than rewritten. With SPLIT, the debug info goes to
# <name>.dbg beside it and the deployed file keeps a .gnu_debuglink to it.
get_filename_component(name "${IN}" NAME)
set(dest "${DEST_DIR}/${name}")
set(tmp "${dest}.new")
file(MAKE_DIRECTORY "${DEST_DIR}")
file(COPY_FILE "${IN}" "${tmp}")
if(SPLIT)
  execute_process(COMMAND "${OBJCOPY}" --only-keep-debug "${tmp}" "${dest}.dbg.new"
                  COMMAND_ERROR_IS_FATAL ANY)
  file(RENAME "${dest}.dbg.new" "${dest}.dbg")
  # The link records the .dbg's basename and CRC; run from DEST_DIR so the
  # basename is <name>.dbg.
  execute_process(COMMAND "${OBJCOPY}" --strip-debug "--add-gnu-debuglink=${name}.dbg" "${tmp}"
                  WORKING_DIRECTORY "${DEST_DIR}" COMMAND_ERROR_IS_FATAL ANY)
  file(SIZE "${IN}" before)
  file(SIZE "${tmp}" after)
  math(EXPR before_kb "${before} / 1024")
  math(EXPR after_kb "${after} / 1024")
  message(STATUS "${name}: ${before_kb} KB -> ${after_kb} KB (debug in ${name}.dbg)")
endif()
file(RENAME "${tmp}" "${dest}")
