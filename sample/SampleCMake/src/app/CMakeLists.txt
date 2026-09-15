add_executable(SampleCMakeApp
    main.cpp
)

target_link_libraries(SampleCMakeApp PRIVATE
    sample_calculator
    sample_core
)

target_compile_features(SampleCMakeApp PRIVATE cxx_std_17)
